using MarketDumper.Services;
using Xunit;

namespace MarketDumper.Tests;

public class RetainerRowVerifierTests
{
    [Theory]
    [InlineData("Grade 2 Shroud Soil | 31 | 1,348", "Grade 2 Shroud Soil", 31, true)]
    [InlineData("grade 2 shroud soil | 31", "Grade 2 Shroud Soil", 31, true)]   // case-insensitive
    [InlineData("Grade 2 Shroud Soil | 99 | 1,348", "Grade 2 Shroud Soil", 31, false)] // wrong qty
    [InlineData("Grade 2 Shroud Soil | 99", "Grade 2 Shroud Soil", 9, false)]   // 9 must not match inside 99
    [InlineData("Other Item | 31", "Grade 2 Shroud Soil", 31, false)]           // wrong item
    [InlineData("", "Grade 2 Shroud Soil", 31, false)]
    [InlineData("Iron Ore Cluster | 5 | 1,234", "Iron Ore", 5, false)]
    [InlineData("Iron Ore | 5 | 1,234", "Iron Ore", 5, true)]
    [InlineData("Iron Ore Cluster | 5", "", 5, false)]
    [InlineData("Grade 2 Shroud Soil | 99", "Grade 2 Shroud Soil", 2, false)]   // digit inside name must not satisfy qty check
    [InlineData("Grade 2 Shroud Soil | 2 | 1,348", "Grade 2 Shroud Soil", 2, true)] // name stripped, qty field still matches
    public void Matches_ChecksNameAndExactQuantityToken(string rowText, string itemName, int qty, bool expected)
    {
        Assert.Equal(expected, RetainerRowVerifier.Matches(rowText, itemName, qty));
    }
}
