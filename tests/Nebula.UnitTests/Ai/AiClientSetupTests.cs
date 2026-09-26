using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Nebula.Application.Ai;
using Nebula.Infrastructure.Ai;

namespace Nebula.UnitTests.Ai;

/// <summary>Locks the DeepSeek wire request: the cost caps must reach the provider in the fields it reads.</summary>
public sealed class AiClientSetupTests
{
    private static async Task<(Uri Uri, JsonElement Body, string? Authorization)> CaptureRequestAsync(AiOptions options)
    {
        using var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        using var client = AiClientSetup.CreateProviderClient(options, new HttpClientPipelineTransport(http));

        var response = await client.GetResponseAsync(
            "hi",
            new ChatOptions { MaxOutputTokens = 123, Tools = [AIFunctionFactory.Create(() => 1, "get_one")] },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        return (handler.Uri!, handler.Body, handler.Authorization);
    }

    [Fact]
    public async Task Requests_go_to_the_configured_endpoint_and_model_with_max_tokens_and_thinking_disabled()
    {
        var (uri, body, authorization) = await CaptureRequestAsync(
            new AiOptions { ApiKey = "sk-test", Endpoint = "https://api.deepseek.com", Model = "deepseek-flash" });

        Assert.Equal("https://api.deepseek.com/chat/completions", uri.ToString());
        Assert.Equal("Bearer sk-test", authorization);
        Assert.Equal("deepseek-flash", body.GetProperty("model").GetString());
        Assert.Equal(123, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.TryGetProperty("max_completion_tokens", out _));
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("get_one", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Thinking_field_can_be_turned_off_for_other_providers()
    {
        var (_, body, _) = await CaptureRequestAsync(new AiOptions { ApiKey = "sk-test", DisableThinking = false });

        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.Equal(123, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Without_a_key_the_client_refuses_to_call_anything()
    {
        using var client = AiClientSetup.CreateProviderClient(new AiOptions { ApiKey = "" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public JsonElement Body { get; private set; }

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Body = document.RootElement.Clone();

            const string completion =
                """
                {
                  "id": "chatcmpl-1", "object": "chat.completion", "created": 1700000000, "model": "deepseek-flash",
                  "choices": [{ "index": 0, "finish_reason": "stop", "message": { "role": "assistant", "content": "ok" } }],
                  "usage": { "prompt_tokens": 10, "completion_tokens": 1, "total_tokens": 11 }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(completion, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
