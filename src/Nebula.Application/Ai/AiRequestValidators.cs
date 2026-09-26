using FluentValidation;
using Nebula.Application.Products;

namespace Nebula.Application.Ai;

/// <summary>Caps on untrusted chat input: they bound both abuse and the prompt size (and so the token cost).</summary>
public sealed class AskRequestValidator : AbstractValidator<AskRequest>
{
    public const int QuestionMax = 500;
    public const int HistoryMaxItems = 6;
    public const int HistoryContentMax = 2000;

    public AskRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.Question)
            .Must(q => !string.IsNullOrWhiteSpace(q)).WithMessage("Question is required")
            .Must(q => q!.Trim().Length <= QuestionMax).WithMessage($"Keep it under {QuestionMax} characters");

        RuleFor(r => r.History)
            .Must(h => h is null || h.Count <= HistoryMaxItems)
            .WithMessage($"Send at most {HistoryMaxItems} previous messages");

        RuleForEach(r => r.History)
            .NotNull().WithMessage("Message is required")
            .SetValidator(new AiChatTurnValidator())
            .When(r => r.History is { Count: <= HistoryMaxItems });
    }

    private sealed class AiChatTurnValidator : AbstractValidator<AiChatTurn>
    {
        public AiChatTurnValidator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(t => t.Role)
                .Must(role => role is "user" or "assistant").WithMessage("Role must be user or assistant");

            RuleFor(t => t.Content)
                .NotNull().WithMessage("Content is required")
                .MaximumLength(HistoryContentMax).WithMessage($"Keep it under {HistoryContentMax} characters");
        }
    }
}

public sealed class ProductDescriptionRequestValidator : AbstractValidator<ProductDescriptionRequest>
{
    public const int NameMax = 120;
    public const int KeywordsMax = 200;

    public ProductDescriptionRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required")
            .Must(name => name!.Trim().Length <= NameMax).WithMessage($"Keep it under {NameMax} characters");

        RuleFor(r => r.Category)
            .Must(category => ProductCategories.TryParse(category, out _)).WithMessage("Unknown category");

        RuleFor(r => r.Keywords)
            .MaximumLength(KeywordsMax).WithMessage($"Keep it under {KeywordsMax} characters");

        RuleFor(r => r.Tone)
            .Must(tone => ProductDescriptionTemplates.TryParseTone(tone, out _))
            .WithMessage("Tone must be friendly, premium or playful");
    }
}
