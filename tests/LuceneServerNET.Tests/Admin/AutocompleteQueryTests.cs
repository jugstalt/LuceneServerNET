using LuceneServerNET.Admin.Services.Import;

namespace LuceneServerNET.Tests.Admin;

public class AutocompleteQueryTests
{
    [Theory]
    [InlineData("haupt", "+haupt*")]
    [InlineData("  Graz   Haupt ", "+graz* +haupt*")]
    [InlineData("8010 leonh 10", "+8010* +leonh* +10*")]
    [InlineData("a:b", "+a\\:b*")]
    [InlineData("c++", "+c\\+\\+*")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Build(string input, string expected)
    {
        Assert.Equal(expected, AutocompleteQuery.Build(input));
    }
}
