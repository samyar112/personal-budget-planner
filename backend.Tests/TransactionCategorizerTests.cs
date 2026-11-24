using backend.Models;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. A Plaid primary and detailed code are kept as the category and subcategory.
 * 2. A Plaid primary without a detailed code keeps a null subcategory.
 * 3. Plaid's category wins over a conflicting keyword.
 * 4. With no Plaid category, a keyword in the description picks the category, case-insensitively.
 * 5. With no Plaid category, the merchant name is matched too.
 * 6. Statement-style descriptions (store numbers, city, state) still match a keyword.
 * 7. Nothing matches: the row is OTHER, never null or empty.
 * 8. An unknown Plaid primary falls back to keywords, then OTHER, so the stored set stays closed.
 * 9. Every category the categorizer can emit is in TransactionCategory.All.
 * 10. Transfer primaries are recognised by IsTransfer; others are not.
 */
public sealed class TransactionCategorizerTests
{
    [Fact]
    public void PlaidCategory_IsKept()
    {
        var result = TransactionCategorizer.Categorize("FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE", "Blue Bottle", "BLUE BOTTLE 12");

        Assert.Equal("FOOD_AND_DRINK", result.Category);
        Assert.Equal("FOOD_AND_DRINK_COFFEE", result.Subcategory);
    }

    [Fact]
    public void PlaidPrimaryOnly_HasNullSubcategory()
    {
        var result = TransactionCategorizer.Categorize("TRAVEL", null, null, "SOMETHING");

        Assert.Equal("TRAVEL", result.Category);
        Assert.Null(result.Subcategory);
    }

    [Fact]
    public void PlaidCategory_WinsOverKeyword()
    {
        var result = TransactionCategorizer.Categorize("ENTERTAINMENT", "ENTERTAINMENT_SPORTING_EVENTS", null, "SHELL ARENA TICKETS");

        Assert.Equal("ENTERTAINMENT", result.Category);
    }

    [Fact]
    public void NoPlaid_KeywordInDescription_PicksCategory_CaseInsensitively()
    {
        var result = TransactionCategorizer.Categorize(null, null, null, "shell oil");

        Assert.Equal("TRANSPORTATION", result.Category);
        Assert.Equal("TRANSPORTATION_GAS", result.Subcategory);
    }

    [Fact]
    public void NoPlaid_KeywordInMerchant_PicksCategory()
    {
        var result = TransactionCategorizer.Categorize(null, null, "Netflix", "NFLX 8821");

        Assert.Equal("ENTERTAINMENT", result.Category);
    }

    [Theory]
    [InlineData("SHELL OIL 5731 WICHITA KS", "TRANSPORTATION")]
    [InlineData("WAL-MART #1234 WICHITA KS", "GENERAL_MERCHANDISE")]
    [InlineData("PAYROLL DIRECT DEP ACME CORP", "INCOME")]
    public void StatementStyleDescriptions_Match(string description, string expected)
    {
        Assert.Equal(expected, TransactionCategorizer.Categorize(null, null, null, description).Category);
    }

    [Fact]
    public void NothingMatches_IsOther()
    {
        var result = TransactionCategorizer.Categorize(null, null, null, "QWERTY ZZZ 99");

        Assert.Equal(TransactionCategory.Other, result.Category);
        Assert.Null(result.Subcategory);
    }

    [Fact]
    public void UnknownPlaidPrimary_FallsBackToKeywordsThenOther()
    {
        Assert.Equal("TRANSPORTATION", TransactionCategorizer.Categorize("BRAND_NEW_CODE", "BRAND_NEW_CODE_X", null, "SHELL OIL").Category);
        Assert.Equal(TransactionCategory.Other, TransactionCategorizer.Categorize("BRAND_NEW_CODE", null, null, "QWERTY").Category);
    }

    [Fact]
    public void EveryEmittedCategory_IsInTheSet()
    {
        string[] descriptions = ["SHELL OIL", "NETFLIX", "WAL-MART", "PAYROLL", "RENT", "QWERTY", "TRANSFER TO SAVINGS"];

        foreach (var description in descriptions)
        {
            var result = TransactionCategorizer.Categorize(null, null, null, description);
            Assert.Contains(result.Category, TransactionCategory.All);
        }
    }

    [Theory]
    [InlineData("TRANSFER_IN", true)]
    [InlineData("TRANSFER_OUT", true)]
    [InlineData("FOOD_AND_DRINK", false)]
    [InlineData(null, false)]
    public void IsTransfer_RecognisesTransferPrimaries(string? category, bool expected)
    {
        Assert.Equal(expected, TransactionCategory.IsTransfer(category));
    }
}
