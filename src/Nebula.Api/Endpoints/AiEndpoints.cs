using Nebula.Api.OpenApi;
using Nebula.Api.Security;
using Nebula.Application.Ai;

namespace Nebula.Api.Endpoints;

public static class AiEndpoints
{
    private const string ModeDescription =
        "`mode` is `live` when the LLM answered, `recorded` when the answer came from templates over live store data "
        + "(no API key configured, daily quota used up, or provider error/timeout). Only live answers use the quota. "
        + "Invalid input ⇒ 400 `validation` with field details.";

    public static RouteGroupBuilder MapAiEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/ai")
            .WithTags("AI")
            .RequireRateLimiting(RateLimitSetup.AiPolicy);

        group.MapGet("/status", (HttpContext http, AiAssistant ai) => ai.Status(RateLimitSetup.ClientKey(http)))
            .WithName("GetAiStatus")
            .WithSummary("AI availability and quota")
            .WithDescription(
                "`enabled` is false (and `provider` null) when no API key is configured; the endpoints then answer in "
                + "recorded mode. `quota.remaining` is the number of live requests left today (UTC) for this client, "
                + "capped by the global daily budget; `quota.limit` is the per-client daily limit.");

        group.MapPost("/ask", (AskRequest? request, HttpContext http, AiAssistant ai, CancellationToken ct) =>
                ai.AskAsync(request!, RateLimitSetup.ClientKey(http), ct))
            .WithName("AskAi")
            .WithSummary("Ask the store analyst")
            .WithDescription(
                "Answers questions about the store's sales, orders, products and customers using read-only data tools "
                + "(`toolsUsed`). `question` is 1..500 characters; `history` holds up to 6 previous turns of at most "
                + "2000 characters. The answer is plain text with simple markdown (paragraphs, `-` lists, `**bold**`). "
                + ModeDescription)
            .Accepts<AskRequest>("application/json")
            .WithRequestExample(
                """
                {
                  "question": "What were my top 5 products this month?",
                  "history": []
                }
                """)
            .WithValidation<AskRequest>("Question is invalid", StatusCodes.Status400BadRequest)
            .Produces<AskResponse>();

        group.MapPost("/product-description", (ProductDescriptionRequest? request, HttpContext http, AiAssistant ai, CancellationToken ct) =>
                ai.DescribeProductAsync(request!, RateLimitSetup.ClientKey(http), ct))
            .WithName("GenerateProductDescription")
            .WithSummary("Write a product description")
            .WithDescription(
                "Short marketing copy (2-4 sentences, at most 600 characters, plain text) from `name` (1..120), "
                + "`category`, optional `keywords` (up to 200 characters) and `tone` (`friendly`, `premium` or `playful`). "
                + ModeDescription)
            .Accepts<ProductDescriptionRequest>("application/json")
            .WithRequestExample(
                """
                {
                  "name": "Nebula Everyday Hoodie",
                  "category": "Apparel",
                  "keywords": "brushed fleece, relaxed fit, recycled cotton",
                  "tone": "friendly"
                }
                """)
            .WithValidation<ProductDescriptionRequest>("Product details are invalid", StatusCodes.Status400BadRequest)
            .Produces<ProductDescriptionResponse>();

        return api;
    }
}
