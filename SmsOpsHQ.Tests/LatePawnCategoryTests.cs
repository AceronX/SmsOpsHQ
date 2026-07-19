using SmsOpsHQ.Api.Controllers;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Desktop.ViewModels;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class LatePawnCategoryTests
{
    [Theory]
    [InlineData("JEWELRY", "JEWELRY")]
    [InlineData("ELECTRONICS", "ELECTRONICS")]
    [InlineData("TOOLS", "TOOLS")]
    public void ParseAggregated_SingleCategory_ReturnsThatCategory(string raw, string expected)
    {
        Assert.Equal(new[] { expected }, LatePawnCategoryRules.ParseAggregated(raw));
    }

    [Fact]
    public void ParseAggregated_MixedDuplicateAndBlankValues_ReturnsDistinctTrimmedCategories()
    {
        IReadOnlyList<string> result = LatePawnCategoryRules.ParseAggregated(
            " JEWELRY | electronics | jewelry |  | GENERAL ");

        Assert.Equal(new[] { "JEWELRY", "electronics", "GENERAL" }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  |  | ")]
    public void ParseAggregated_BlankValues_ReturnsEmpty(string? raw)
    {
        Assert.Empty(LatePawnCategoryRules.ParseAggregated(raw));
    }

    [Fact]
    public void RiskScore_MixedJewelryAndElectronics_UsesHighestCategoryRule()
    {
        int jewelryOnly = CustomersController.CalculateRiskScore(1, 0, new[] { "JEWELRY" });
        int mixed = CustomersController.CalculateRiskScore(1, 0, new[] { "JEWELRY", "ELECTRONICS" });
        int mixedWithGeneral = CustomersController.CalculateRiskScore(1, 0, new[] { "JEWELRY", "GENERAL" });

        Assert.Equal(15, jewelryOnly);
        Assert.Equal(20, mixed);
        Assert.Equal(20, mixedWithGeneral);
    }

    [Fact]
    public void CategoryFilters_MixedTicketMatchesJewelryAndElectronics()
    {
        LateCustomerItem mixed = Customer("Alex Mixed", "JEWELRY", "ELECTRONICS");

        Assert.True(LateCustomersViewModel.MatchesFilters(mixed, "Jewelry", null));
        Assert.True(LateCustomersViewModel.MatchesFilters(mixed, "Electronics", null));
        Assert.False(LateCustomersViewModel.MatchesFilters(mixed, "Other", null));
        Assert.Equal(mixed.Categories, mixed.CategoryChips);
    }

    [Fact]
    public void CategoryFilters_OtherMatchesNeitherJewelryNorElectronics()
    {
        LateCustomerItem other = Customer("Taylor Tools", "TOOLS");
        LateCustomerItem uncategorized = Customer("Casey Blank");

        Assert.True(LateCustomersViewModel.MatchesFilters(other, "Other", null));
        Assert.True(LateCustomersViewModel.MatchesFilters(uncategorized, "Other", null));
        Assert.False(LateCustomersViewModel.MatchesFilters(other, "Jewelry", null));
        Assert.False(LateCustomersViewModel.MatchesFilters(other, "Electronics", null));
    }

    [Fact]
    public void CategoryAndSearchFilters_AreCombinedAndCategoryTextIsSearchable()
    {
        LateCustomerItem mixed = Customer("Alex Mixed", "JEWELRY", "ELECTRONICS");
        LateCustomerItem electronicsOnly = Customer("Erin Radio", "ELECTRONICS");

        Assert.True(LateCustomersViewModel.MatchesFilters(mixed, "Jewelry", "electronics"));
        Assert.False(LateCustomersViewModel.MatchesFilters(electronicsOnly, "Jewelry", "electronics"));
        Assert.False(LateCustomersViewModel.MatchesFilters(mixed, "Jewelry", "no-match"));
    }

    private static LateCustomerItem Customer(string fullName, params string[] categories)
    {
        string[] names = fullName.Split(' ', 2);
        return new LateCustomerItem
        {
            FirstName = names[0],
            LastName = names.Length > 1 ? names[1] : string.Empty,
            Categories = categories,
            Category = string.Join(" | ", categories)
        };
    }
}
