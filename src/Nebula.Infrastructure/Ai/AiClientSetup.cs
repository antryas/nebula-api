using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nebula.Application.Ai;
using OpenAI;
using OpenAI.Chat;

namespace Nebula.Infrastructure.Ai;

/// <summary>
/// Builds the <see cref="IChatClient"/> the assistant talks to: the provider client (DeepSeek through its
/// OpenAI-compatible API) wrapped in automatic function invocation. Switching provider means replacing
/// <see cref="CreateProviderClient"/> with another <c>IChatClient</c> adapter (OpenAI, Azure OpenAI, Anthropic, ...);
/// Application code does not change.
/// </summary>
public static class AiClientSetup
{
    /// <summary>Keyed registration of the raw provider client; tests replace it with a fake to exercise the pipeline.</summary>
    public const string ProviderClientKey = "ai-provider";

    public static IServiceCollection AddAiClient(this IServiceCollection services)
    {
        // Bound lazily so hosts (and tests) can override Ai:* after registration.
        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.SectionName);

        services.AddKeyedSingleton<IChatClient>(ProviderClientKey, (sp, _) =>
            CreateProviderClient(sp.GetRequiredService<IOptions<AiOptions>>().Value));

        services
            .AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ProviderClientKey))
            .Use((inner, sp) => new FunctionInvokingChatClient(inner, sp.GetService<ILoggerFactory>(), sp)
            {
                MaximumIterationsPerRequest = sp.GetRequiredService<IOptions<AiOptions>>().Value.EffectiveMaxToolIterations,
                // Tool errors go back to the model as a generic message, never with exception details.
                IncludeDetailedErrors = false,
            });

        return services;
    }

    /// <summary>
    /// The provider client for <paramref name="options"/>. <paramref name="transport"/> replaces the HTTP pipeline
    /// transport (tests use it to inspect the wire request).
    /// </summary>
    public static IChatClient CreateProviderClient(AiOptions options, PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            // Recorded mode never calls the provider; this keeps the dependency graph valid without a key.
            return new NotConfiguredChatClient();
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint),
                NetworkTimeout = options.Timeout,
                // One quick retry at most: the request as a whole is bounded by AiOptions.Timeout anyway.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
                Transport = transport,
            });
        var disableThinking = options.DisableThinking;
        return client.GetChatClient(options.Model)
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(chatOptions => ApplyProviderFields(chatOptions, disableThinking))
            .Build();
    }

    /// <summary>
    /// Request fields the OpenAI SDK does not send the way DeepSeek expects: the output cap goes out as
    /// <c>max_tokens</c> (the SDK would send <c>max_completion_tokens</c>, which DeepSeek does not read), and
    /// optionally <c>thinking: { type: "disabled" }</c> so no money is spent on reasoning tokens.
    /// </summary>
    private static void ApplyProviderFields(ChatOptions chatOptions, bool disableThinking)
    {
        var maxTokens = chatOptions.MaxOutputTokens;
        chatOptions.MaxOutputTokens = null;
        chatOptions.RawRepresentationFactory = _ =>
        {
            var completion = new ChatCompletionOptions();
#pragma warning disable SCME0001 // JsonPatch is the SDK's supported way to send provider-specific fields.
            if (maxTokens is { } limit)
            {
                completion.Patch.Set("$.max_tokens"u8, limit);
            }

            if (disableThinking)
            {
                completion.Patch.Set("$.thinking.type"u8, "disabled");
            }
#pragma warning restore SCME0001
            return completion;
        };
    }

    private sealed class NotConfiguredChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No AI provider is configured (Ai:ApiKey is empty).");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No AI provider is configured (Ai:ApiKey is empty).");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
