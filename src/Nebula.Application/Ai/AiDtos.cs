namespace Nebula.Application.Ai;

/// <summary>Live requests left today for the caller: the smaller of the per-client and the global allowance.</summary>
public sealed record AiQuota(int Remaining, int Limit);

/// <summary><c>GET /api/ai/status</c>. <c>Provider</c> is null when no API key is configured.</summary>
public sealed record AiStatus(bool Enabled, string? Provider, AiQuota Quota);

/// <summary>
/// Body of <c>POST /api/ai/ask</c>. Nullable so that missing fields reach the validator instead of failing binding.
/// </summary>
public sealed record AskRequest(string? Question, IReadOnlyList<AiChatTurn>? History);

/// <summary>A previous chat turn. <c>Role</c>: user | assistant.</summary>
public sealed record AiChatTurn(string? Role, string? Content);

/// <summary>
/// <c>Mode</c>: live | recorded. <c>Answer</c> is plain text with simple markdown (paragraphs, <c>-</c> lists,
/// <c>**bold**</c>). <c>ToolsUsed</c> lists the store data tools behind the answer.
/// </summary>
public sealed record AskResponse(string Answer, string Mode, IReadOnlyList<string> ToolsUsed, AiQuota Quota);

/// <summary>Body of <c>POST /api/ai/product-description</c>. <c>Tone</c>: friendly | premium | playful.</summary>
public sealed record ProductDescriptionRequest(string? Name, string? Category, string? Keywords, string? Tone);

/// <summary>Plain-text description of 2–4 sentences, at most 600 characters. <c>Mode</c>: live | recorded.</summary>
public sealed record ProductDescriptionResponse(string Description, string Mode, AiQuota Quota);

/// <summary>Wire values of the response <c>mode</c> field.</summary>
public static class AiModes
{
    public const string Live = "live";
    public const string Recorded = "recorded";
}
