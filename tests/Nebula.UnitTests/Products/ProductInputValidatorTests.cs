using Nebula.Application.Products;

namespace Nebula.UnitTests.Products;

public sealed class ProductInputValidatorTests
{
    private static readonly ProductInputValidator Validator = new();

    internal static ProductInput Valid() => new(
        Sku: "APP-NEW-001",
        Name: "Linen Shirt",
        Description: "Breathable.",
        Category: "Apparel",
        Price: 49.9m,
        CompareAtPrice: null,
        ImageUrl: "",
        Stock: 5,
        Variants: [],
        Active: true);

    private static void AssertSingleError(ProductInput input, string property, string message)
    {
        var result = Validator.Validate(input);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == property);
        Assert.Equal(message, error.ErrorMessage);
    }

    [Fact]
    public void Valid_input_passes()
    {
        Assert.True(Validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Minimal_input_with_only_mock_required_fields_passes()
    {
        var input = new ProductInput("SKU1", "Name", null, "Home", 1m, null, null, null, null, null);

        Assert.True(Validator.Validate(input).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        AssertSingleError(Valid() with { Name = name }, "Name", "Name is required");
    }

    [Fact]
    public void Name_is_capped_like_the_form()
    {
        AssertSingleError(Valid() with { Name = new string('a', 81) }, "Name", "Keep it under 80 characters");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-5")]
    public void Price_must_be_greater_than_zero(string? price)
    {
        var value = price is null ? (decimal?)null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        AssertSingleError(Valid() with { Price = value }, "Price", "Price must be greater than 0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Sku_is_required(string? sku)
    {
        AssertSingleError(Valid() with { Sku = sku }, "Sku", "SKU is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Toys")]
    [InlineData("apparel")]
    [InlineData("1")]
    public void Category_must_be_known(string? category)
    {
        AssertSingleError(Valid() with { Category = category }, "Category", "Unknown category");
    }

    [Theory]
    [InlineData("49.9")]
    [InlineData("10")]
    public void Compare_at_price_must_exceed_price(string compareAt)
    {
        var value = decimal.Parse(compareAt, System.Globalization.CultureInfo.InvariantCulture);

        AssertSingleError(
            Valid() with { CompareAtPrice = value }, "CompareAtPrice", "Compare-at price must be greater than price");
    }

    [Fact]
    public void Compare_at_price_above_price_passes()
    {
        Assert.True(Validator.Validate(Valid() with { CompareAtPrice = 59.9m }).IsValid);
    }

    [Fact]
    public void Compare_at_price_is_not_checked_without_a_price()
    {
        var result = Validator.Validate(Valid() with { Price = null, CompareAtPrice = 5m });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "CompareAtPrice");
    }

    [Fact]
    public void Description_is_capped_like_the_form()
    {
        AssertSingleError(
            Valid() with { Description = new string('d', 1001) }, "Description", "Keep it under 1000 characters");
    }

    [Fact]
    public void Reports_every_invalid_field()
    {
        var result = Validator.Validate(new ProductInput(null, null, null, null, null, null, null, null, null, null));

        Assert.Equal(
            ["Category", "Name", "Price", "Sku"],
            result.Errors.Select(e => e.PropertyName).Order(StringComparer.Ordinal));
    }
}
