using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Nebula.IntegrationTests.Infrastructure;

/// <summary>
/// Scripted stand-in for the provider client, registered under the provider key so the real function-invocation
/// pipeline runs on top of it. The last user message picks the script:
/// "top" ⇒ calls <c>get_top_products</c>, then answers with the tool result; "fail" ⇒ throws;
/// "slow" ⇒ never answers (timeout); anything else ⇒ a plain live answer. Calls without tools get product copy.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public const string PlainAnswer = "Live answer from the fake model.";
    public const string Description = "**Cozy** fleece for cold days.\n\nIt pairs with everything.";

    public ConcurrentQueue<ChatOptions?> Calls { get; } = new();

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(options);
        var list = messages.ToList();

        var toolResult = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
        if (toolResult is not null)
        {
            var json = toolResult.Result is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(toolResult.Result);
            return Reply($"From the store data: {json}");
        }

        var question = list.Last(m => m.Role == ChatRole.User).Text;
        if (options?.Tools is not { Count: > 0 })
        {
            return Reply(Description);
        }

        if (question.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Provider exploded with secret details");
        }

        if (question.Contains("slow", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (question.Contains("top", StringComparison.OrdinalIgnoreCase))
        {
            var call = new FunctionCallContent(
                "call_1", "get_top_products", new Dictionary<string, object?> { ["range"] = "30d", ["limit"] = 1 });
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10, TotalTokenCount = 110 },
            };
        }

        return Reply(PlainAnswer);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static ChatResponse Reply(string text) => new(new ChatMessage(ChatRole.Assistant, text))
    {
        Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 20, TotalTokenCount = 220 },
    };
}
