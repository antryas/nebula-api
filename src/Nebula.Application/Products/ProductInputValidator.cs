using FluentValidation;

namespace Nebula.Application.Products;

/// <summary>
/// Rules and messages of <c>validate()</c> in <c>mock-api/handlers/products.ts</c>, plus the product form's length
/// caps. SKU uniqueness needs the database and the edited product's id, so <see cref="ProductsService"/> checks it.
/// </summary>
public sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    /// <summary>Same caps as <c>NAME_MAX</c> / <c>DESCRIPTION_MAX</c> in <c>product-form.ts</c>.</summary>
    public const int NameMax = 80;
    public const int DescriptionMax = 1000;

    public ProductInputValidator()
    {
        // First failing check per field only, like the mock's single message per key.
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(p => p.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required")
            .Must(name => name!.Trim().Length <= NameMax).WithMessage($"Keep it under {NameMax} characters");

        RuleFor(p => p.Price)
            .Must(price => price > 0).WithMessage("Price must be greater than 0");

        RuleFor(p => p.Sku)
            .Must(sku => !string.IsNullOrWhiteSpace(sku)).WithMessage("SKU is required");

        RuleFor(p => p.Category)
            .Must(category => ProductCategories.TryParse(category, out _)).WithMessage("Unknown category");

        RuleFor(p => p.CompareAtPrice)
            .Must((p, compareAt) => compareAt > p.Price)
            .When(p => p.CompareAtPrice is not null && p.Price is not null)
            .WithMessage("Compare-at price must be greater than price");

        RuleFor(p => p.Description)
            .MaximumLength(DescriptionMax).WithMessage($"Keep it under {DescriptionMax} characters");
    }
}
