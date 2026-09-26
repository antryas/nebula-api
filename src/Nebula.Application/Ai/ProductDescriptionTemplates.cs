using System.Text;
using Nebula.Domain;

namespace Nebula.Application.Ai;

/// <summary>Voice of a generated product description; wire values <c>friendly</c>, <c>premium</c>, <c>playful</c>.</summary>
public enum DescriptionTone
{
    Friendly,
    Premium,
    Playful,
}

/// <summary>
/// Recorded-mode product copy: a deterministic template per tone filled with the name, category and up to three
/// keywords. Also holds the shared rules for the length and shape of any description (live or recorded).
/// </summary>
public static class ProductDescriptionTemplates
{
    public const int MaxLength = 600;
    private const int MaxKeywords = 3;

    public static bool TryParseTone(string? value, out DescriptionTone tone)
    {
        switch (value)
        {
            case "friendly":
                tone = DescriptionTone.Friendly;
                return true;
            case "premium":
                tone = DescriptionTone.Premium;
                return true;
            case "playful":
                tone = DescriptionTone.Playful;
                return true;
            default:
                tone = default;
                return false;
        }
    }

    public static string ToWire(this DescriptionTone tone) => tone switch
    {
        DescriptionTone.Friendly => "friendly",
        DescriptionTone.Premium => "premium",
        DescriptionTone.Playful => "playful",
        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, null),
    };

    /// <summary>Two sentences, or three when keywords are given.</summary>
    public static string Render(string name, ProductCategory category, string? keywords, DescriptionTone tone)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        var kind = category.ToString().ToLowerInvariant();
        var words = SplitKeywords(keywords);
        var list = JoinList(words);

        var text = tone switch
        {
            DescriptionTone.Premium =>
                $"The {name} brings refined craftsmanship to our {kind} collection."
                + (words.Count > 0 ? $" It is defined by {list}." : "")
                + " Every detail is considered, so it feels as exceptional as it looks.",
            DescriptionTone.Playful =>
                $"Say hello to the {name}, the {kind} pick that is here to have some fun!"
                + (words.Count > 0 ? $" Think {list} — all in one happy package." : "")
                + " Go on, treat yourself.",
            _ =>
                $"Meet the {name}, an easy {kind} favorite made for everyday life."
                + (words.Count > 0 ? $" You will love its {list}." : "")
                + " It is simple to enjoy from day one and ready whenever you are.",
        };

        return Clean(text);
    }

    /// <summary>
    /// Plain text on one paragraph, at most <see cref="MaxLength"/> characters: collapses whitespace, drops markdown
    /// emphasis (<c>*</c>, backticks) and wrapping quotes, and cuts over-long text at the last full sentence that fits.
    /// </summary>
    public static string Clean(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in text ?? "")
        {
            if (c is '*' or '`')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(c);
        }

        var result = builder.ToString().Trim().Trim('"', '“', '”').Trim();
        if (result.Length <= MaxLength)
        {
            return result;
        }

        var cut = result[..MaxLength];
        var end = cut.LastIndexOfAny(['.', '!', '?']);
        return end > 0 ? cut[..(end + 1)] : cut.TrimEnd();
    }

    private static List<string> SplitKeywords(string? keywords) =>
    [
        .. (keywords ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxKeywords),
    ];

    private static string JoinList(List<string> words) => words.Count switch
    {
        0 => "",
        1 => words[0],
        _ => $"{string.Join(", ", words[..^1])} and {words[^1]}",
    };
}
