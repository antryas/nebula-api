using Nebula.Application.Common;

namespace Nebula.UnitTests.Common;

public sealed class ListQueryTests
{
    [Theory]
    [InlineData("0", "500", null, "up", null, 1, 100, null, null, null)]
    [InlineData(null, null, null, null, null, 1, 20, null, null, null)]
    [InlineData("", " ", "", "", "  ", 1, 20, null, null, null)]
    [InlineData("3", "25", "total", "asc", "ann", 3, 25, "total", "asc", "ann")]
    [InlineData("abc", "0", "number", "desc", "x", 1, 1, "number", "desc", "x")]
    [InlineData("2.9", "15px", null, "ASC", null, 2, 15, null, null, null)]
    [InlineData("-4", "-1", null, null, null, 1, 1, null, null, null)]
    public void Parse_ports_mock_defaults_and_clamps(
        string? page, string? pageSize, string? sort, string? dir, string? search,
        int expectedPage, int expectedSize, string? expectedSort, string? expectedDir, string? expectedSearch)
    {
        var q = ListQuery.Parse(page, pageSize, sort, dir, search);

        Assert.Equal(expectedPage, q.Page);
        Assert.Equal(expectedSize, q.PageSize);
        Assert.Equal(expectedSort, q.Sort);
        Assert.Equal(expectedDir, q.Dir);
        Assert.Equal(expectedSearch, q.Search);
    }

    [Fact]
    public void Parse_saturates_huge_page_numbers()
    {
        var q = ListQuery.Parse("99999999999999999999", null, null, null, null);

        Assert.Equal(int.MaxValue, q.Page);
    }
}
