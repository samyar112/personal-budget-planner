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
 * 11. Keyword transfers follow the direction of the money: in is TRANSFER_IN, out is TRANSFER_OUT.
 * 12. Payroll wins over a transfer keyword in the same description.
 * 13. Outgoing P2P payments and ATM withdrawals get the subcategories that summaries count as spending.
 * 14. BP matches only as a fuel-station descriptor, not as any whole word.
 * 15. Plaid v2 codes are mapped to the v1 set: INCOME_SALARY to INCOME_WAGES, LOAN_DISBURSEMENTS to TRANSFER_IN.
 */
public sealed class TransactionCategorizerTests
{
    [Fact]
    public void PlaidCategory_IsKept()
    {
        var result = TransactionCategorizer.Categorize("FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE", "Blue Bottle", "BLUE BOTTLE 12", -10m);

        Assert.Equal("FOOD_AND_DRINK", result.Category);
        Assert.Equal("FOOD_AND_DRINK_COFFEE", result.Subcategory);
    }

    [Fact]
    public void PlaidPrimaryOnly_HasNullSubcategory()
    {
        var result = TransactionCategorizer.Categorize("TRAVEL", null, null, "SOMETHING", -10m);

        Assert.Equal("TRAVEL", result.Category);
        Assert.Null(result.Subcategory);
    }

    [Fact]
    public void PlaidCategory_WinsOverKeyword()
    {
        var result = TransactionCategorizer.Categorize("ENTERTAINMENT", "ENTERTAINMENT_SPORTING_EVENTS", null, "SHELL ARENA TICKETS", -10m);

        Assert.Equal("ENTERTAINMENT", result.Category);
    }

    [Fact]
    public void NoPlaid_KeywordInDescription_PicksCategory_CaseInsensitively()
    {
        var result = TransactionCategorizer.Categorize(null, null, null, "shell oil", -10m);

        Assert.Equal("TRANSPORTATION", result.Category);
        Assert.Equal("TRANSPORTATION_GAS", result.Subcategory);
    }

    [Fact]
    public void NoPlaid_KeywordInMerchant_PicksCategory()
    {
        var result = TransactionCategorizer.Categorize(null, null, "Netflix", "NFLX 8821", -10m);

        Assert.Equal("ENTERTAINMENT", result.Category);
    }

    [Theory]
    [InlineData("SHELL OIL 5731 WICHITA KS", -40, "TRANSPORTATION")]
    [InlineData("WAL-MART #1234 WICHITA KS", -25, "GENERAL_MERCHANDISE")]
    [InlineData("PAYROLL DIRECT DEP ACME CORP", 3000, "INCOME")]
    public void StatementStyleDescriptions_Match(string description, decimal amount, string expected)
    {
        Assert.Equal(expected, TransactionCategorizer.Categorize(null, null, null, description, amount).Category);
    }

    [Fact]
    public void NothingMatches_IsOther()
    {
        var result = TransactionCategorizer.Categorize(null, null, null, "QWERTY ZZZ 99", -10m);

        Assert.Equal(TransactionCategory.Other, result.Category);
        Assert.Null(result.Subcategory);
    }

    [Fact]
    public void UnknownPlaidPrimary_FallsBackToKeywordsThenOther()
    {
        Assert.Equal("TRANSPORTATION", TransactionCategorizer.Categorize("BRAND_NEW_CODE", "BRAND_NEW_CODE_X", null, "SHELL OIL", -10m).Category);
        Assert.Equal(TransactionCategory.Other, TransactionCategorizer.Categorize("BRAND_NEW_CODE", null, null, "QWERTY", -10m).Category);
    }

    [Fact]
    public void EveryEmittedCategory_IsInTheSet()
    {
        string[] descriptions = ["SHELL OIL", "NETFLIX", "WAL-MART", "PAYROLL", "RENT", "QWERTY", "TRANSFER TO SAVINGS"];

        foreach (var description in descriptions)
        {
            var result = TransactionCategorizer.Categorize(null, null, null, description, -10m);
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

    [Theory]
    [InlineData("ONLINE TRANSFER FROM SAVINGS", 200, "TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER")]
    [InlineData("ONLINE TRANSFER TO SAVINGS", -200, "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER")]
    [InlineData("ZELLE FROM JOHN", 40, "TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER")]
    public void KeywordTransfers_FollowTheDirectionOfMoney(string description, decimal amount, string category, string subcategory)
    {
        var result = TransactionCategorizer.Categorize(null, null, null, description, amount);

        Assert.Equal((category, subcategory), (result.Category, result.Subcategory));
    }

    [Fact]
    public void Payroll_WinsOverTransferKeyword()
    {
        var result = TransactionCategorizer.Categorize(null, null, null, "ACH TRANSFER PAYROLL ACME", 3000m);

        Assert.Equal(("INCOME", "INCOME_WAGES"), (result.Category, result.Subcategory));
    }

    [Theory]
    [InlineData("ZELLE TO LANDLORD RENT", "TRANSFER_OUT_OTHER_TRANSFER_OUT")]
    [InlineData("VENMO PAYMENT 1029", "TRANSFER_OUT_OTHER_TRANSFER_OUT")]
    [InlineData("ATM WITHDRAWAL 0042 WICHITA KS", "TRANSFER_OUT_WITHDRAWAL")]
    public void OutgoingP2PAndCash_AreCountedSubcategories(string description, string subcategory)
    {
        var result = TransactionCategorizer.Categorize(null, null, null, description, -500m);

        Assert.Equal(("TRANSFER_OUT", subcategory), (result.Category, result.Subcategory));
    }

    [Theory]
    [InlineData("BP#8841123 WICHITA KS", "TRANSPORTATION")]
    [InlineData("BP OIL 5531", "TRANSPORTATION")]
    [InlineData("CHASE BP REWARDS", "OTHER")]
    public void Bp_MatchesOnlyAsFuelStation(string description, string expected)
    {
        Assert.Equal(expected, TransactionCategorizer.Categorize(null, null, null, description, -30m).Category);
    }

    [Theory]
    [InlineData("INCOME", "INCOME_SALARY", 3000, "INCOME", "INCOME_WAGES")]
    [InlineData("LOAN_DISBURSEMENTS", "LOAN_DISBURSEMENTS_PERSONAL", 5000, "TRANSFER_IN", "TRANSFER_IN_CASH_ADVANCES_AND_LOANS")]
    [InlineData("LOAN_DISBURSEMENTS", null, 5000, "TRANSFER_IN", "TRANSFER_IN_CASH_ADVANCES_AND_LOANS")]
    public void PlaidV2Codes_MapToTheV1Set(string primary, string? detailed, decimal amount, string category, string subcategory)
    {
        var result = TransactionCategorizer.Categorize(primary, detailed, null, "LENDER DEPOSIT", amount);

        Assert.Equal((category, subcategory), (result.Category, result.Subcategory));
    }
}
