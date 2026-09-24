namespace Nebula.Infrastructure.Persistence.Configurations;

internal static class OwnedKeys
{
    /// <summary>Shadow auto-increment key of owned collection rows.</summary>
    public const string RowId = "RowId";
}

internal static class Collations
{
    /// <summary>SQLite built-in case-insensitive (ASCII) collation.</summary>
    public const string NoCase = "NOCASE";
}
