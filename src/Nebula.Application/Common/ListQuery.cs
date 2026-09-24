namespace Nebula.Application.Common;

/// <summary>
/// Common list parameters, parsed leniently like <c>parseListQuery</c> + <c>applyListQuery</c> in the
/// Angular mock (<c>mock-api/query.ts</c>): bad values fall back to defaults and are clamped.
/// </summary>
public sealed record ListQuery(int Page, int PageSize, string? Sort, string? Dir, string? Search)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static ListQuery Default { get; } = new(1, DefaultPageSize, null, null, null);

    /// <summary>True when the rows should be sorted descending (the mock's default when <c>dir</c> is missing).</summary>
    public bool Descending => Dir != "asc";

    /// <summary>
    /// Page ≥ 1 (default 1), page size 1..100 (default 20), <paramref name="dir"/> only <c>asc</c>/<c>desc</c>
    /// (anything else ⇒ null), blank strings ⇒ null. Integers are read like JavaScript <c>parseInt</c>.
    /// </summary>
    public static ListQuery Parse(string? page, string? pageSize, string? sort, string? dir, string? search) => new(
        (int)Math.Max(1, ParseJsInt(page) ?? 1),
        (int)Math.Clamp(ParseJsInt(pageSize) ?? DefaultPageSize, 1, MaxPageSize),
        Blank(sort),
        dir is "asc" or "desc" ? dir : null,
        Blank(search));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// JavaScript <c>parseInt(raw, 10)</c>: leading whitespace, optional sign, then the longest digit prefix
    /// ("2.9" ⇒ 2, "15px" ⇒ 15, "abc" ⇒ null). Saturates to the <see cref="int"/> range.
    /// </summary>
    internal static long? ParseJsInt(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var s = raw.AsSpan().TrimStart();
        var negative = false;
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
        {
            negative = s[0] == '-';
            s = s[1..];
        }

        var digits = 0;
        long value = 0;
        while (digits < s.Length && char.IsAsciiDigit(s[digits]))
        {
            value = Math.Min(int.MaxValue, (value * 10) + (s[digits] - '0'));
            digits++;
        }

        return digits == 0 ? null : negative ? -value : value;
    }
}
