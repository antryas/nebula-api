namespace Nebula.Application.Ai;

/// <summary>
/// <c>Ai</c> configuration section. Without <see cref="ApiKey"/> the assistant runs in recorded mode: no provider
/// calls, templated answers built from live store data.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Upper bound for <see cref="MaxOutputTokens"/>, whatever the configuration says.</summary>
    public const int MaxOutputTokensCap = 2000;

    /// <summary>Upper bound for <see cref="MaxToolIterations"/>.</summary>
    public const int MaxToolIterationsCap = 8;

    /// <summary>Provider API key, from the environment (<c>Ai__ApiKey</c>). Never committed, never logged.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>OpenAI-compatible base URL of the provider.</summary>
    public string Endpoint { get; set; } = "https://api.deepseek.com";

    /// <summary>Chat model id; it must support tool calling.</summary>
    public string Model { get; set; } = "deepseek-flash";

    /// <summary>Display name reported by <c>GET /api/ai/status</c>.</summary>
    public string Provider { get; set; } = "DeepSeek";

    /// <summary>
    /// Sends <c>thinking: { type: "disabled" }</c> (DeepSeek extension): reasoning tokens would cost money and eat
    /// the output budget. Turn off for providers that reject unknown request fields.
    /// </summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>Output budget per provider call; clamped to 1..<see cref="MaxOutputTokensCap"/>.</summary>
    public int MaxOutputTokens { get; set; } = 600;

    /// <summary>Live requests per client IP per UTC day.</summary>
    public int DailyRequestsPerClient { get; set; } = 20;

    /// <summary>Live requests for all clients together per UTC day.</summary>
    public int DailyRequestsTotal { get; set; } = 500;

    /// <summary>Model ⇄ tool round trips per question; clamped to 1..<see cref="MaxToolIterationsCap"/>.</summary>
    public int MaxToolIterations { get; set; } = 4;

    /// <summary>Budget for one live request including tool round trips; slower answers fall back to recorded mode.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

    public int EffectiveMaxOutputTokens => Math.Clamp(MaxOutputTokens, 1, MaxOutputTokensCap);

    public int EffectiveMaxToolIterations => Math.Clamp(MaxToolIterations, 1, MaxToolIterationsCap);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));
}
