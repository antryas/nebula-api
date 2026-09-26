using Nebula.Application.Ai;
using Nebula.Domain;

namespace Nebula.UnitTests.Ai;

public sealed class ProductDescriptionTemplatesTests
{
    private static int Sentences(string text) => text.Count(c => c is '.' or '!' or '?');

    [Theory]
    [InlineData(DescriptionTone.Friendly, "Meet the Nebula Hoodie, an easy apparel favorite")]
    [InlineData(DescriptionTone.Premium, "The Nebula Hoodie brings refined craftsmanship to our apparel collection.")]
    [InlineData(DescriptionTone.Playful, "Say hello to the Nebula Hoodie, the apparel pick")]
    public void Each_tone_has_its_own_template(DescriptionTone tone, string start)
    {
        var text = ProductDescriptionTemplates.Render("Nebula Hoodie", ProductCategory.Apparel, "", tone);

        Assert.StartsWith(start, text, StringComparison.Ordinal);
        Assert.Equal(2, Sentences(text));
    }

    [Fact]
    public void Keywords_become_an_extra_sentence_with_at_most_three_distinct_items()
    {
        var text = ProductDescriptionTemplates.Render(
            "  Nebula Hoodie ", ProductCategory.Apparel, "fleece, relaxed fit; Fleece, recycled cotton, pockets", DescriptionTone.Premium);

        Assert.Equal(
            "The Nebula Hoodie brings refined craftsmanship to our apparel collection. "
            + "It is defined by fleece, relaxed fit and recycled cotton. "
            + "Every detail is considered, so it feels as exceptional as it looks.",
            text);
        Assert.Equal(3, Sentences(text));
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var first = ProductDescriptionTemplates.Render("Lamp", ProductCategory.Home, "warm light", DescriptionTone.Playful);
        var second = ProductDescriptionTemplates.Render("Lamp", ProductCategory.Home, "warm light", DescriptionTone.Playful);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Longest_inputs_stay_within_600_characters()
    {
        var text = ProductDescriptionTemplates.Render(
            new string('N', 120), ProductCategory.Electronics, string.Join(", ", Enumerable.Repeat(new string('k', 64), 3)), DescriptionTone.Friendly);

        Assert.InRange(text.Length, 1, ProductDescriptionTemplates.MaxLength);
    }

    [Theory]
    [InlineData("friendly", DescriptionTone.Friendly)]
    [InlineData("premium", DescriptionTone.Premium)]
    [InlineData("playful", DescriptionTone.Playful)]
    public void Tones_parse_from_wire_values(string wire, DescriptionTone expected)
    {
        Assert.True(ProductDescriptionTemplates.TryParseTone(wire, out var tone));
        Assert.Equal(expected, tone);
        Assert.Equal(wire, tone.ToWire());
    }

    [Theory]
    [InlineData("Friendly")]
    [InlineData("formal")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_tones_are_rejected(string? wire) =>
        Assert.False(ProductDescriptionTemplates.TryParseTone(wire, out _));

    [Fact]
    public void Clean_flattens_markdown_and_quotes()
    {
        var text = ProductDescriptionTemplates.Clean("\"**Soft** and\n\n`warm`.  Made to last.\"");

        Assert.Equal("Soft and warm. Made to last.", text);
    }

    [Fact]
    public void Clean_cuts_long_text_at_the_last_full_sentence()
    {
        var sentence = new string('a', 99) + ". ";
        var text = ProductDescriptionTemplates.Clean(string.Concat(Enumerable.Repeat(sentence, 10)));

        Assert.True(text.Length <= ProductDescriptionTemplates.MaxLength);
        Assert.EndsWith(".", text, StringComparison.Ordinal);
        Assert.Equal(5, Sentences(text));
    }
}
