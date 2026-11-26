using System.Text.RegularExpressions;
using backend.Models;

namespace backend.Services;

public sealed record Categorization(string Category, string? Subcategory);

/// <summary>
/// Assigns every ledger row a category. Order: Plaid's own category, then keyword rules, then
/// <see cref="TransactionCategory.Other"/>. Never returns null.
/// </summary>
public static class TransactionCategorizer
{
    private enum Direction { Any, In, Out }

    private sealed record Rule(Regex Pattern, string Category, string? Subcategory, Direction Direction);

    // Ordered: the first match wins, so put specific words before general ones. Whole words only,
    // so "RENT" does not match "CURRENT". Payroll comes before transfer, so "ACH TRANSFER PAYROLL" is income.
    // Money sent to people and cash taken out get the subcategories summaries count as spending.
    private static readonly Rule[] Rules =
    [
        Keyword("PAYROLL|DIRECT DEP|SALARY", TransactionCategory.Income, "INCOME_WAGES", Direction.In),
        Keyword("ATM WITHDRAWAL|ATM W/D|CASH WITHDRAWAL", TransactionCategory.TransferOut, TransactionCategory.CashWithdrawal, Direction.Out),
        Keyword("ZELLE|VENMO|CASH APP", TransactionCategory.TransferOut, TransactionCategory.OtherTransferOut, Direction.Out),
        Keyword("TRANSFER|ZELLE|VENMO|CASH APP|PAYMENT THANK YOU", TransactionCategory.TransferIn, "TRANSFER_IN_ACCOUNT_TRANSFER", Direction.In),
        Keyword("TRANSFER|PAYMENT THANK YOU", TransactionCategory.TransferOut, "TRANSFER_OUT_ACCOUNT_TRANSFER", Direction.Out),
        Keyword(@"SHELL|CHEVRON|EXXON|BP OIL|BP PRODUCTS|BP#?\d+|FUEL|GAS STATION", TransactionCategory.Transportation, "TRANSPORTATION_GAS"),
        Keyword("UBER|LYFT|PARKING|TRANSIT", TransactionCategory.Transportation, null),
        Keyword("NETFLIX|NFLX|SPOTIFY|HULU|DISNEY|CINEMA|THEATER", TransactionCategory.Entertainment, null),
        Keyword("STARBUCKS|COFFEE|CAFE", TransactionCategory.FoodAndDrink, "FOOD_AND_DRINK_COFFEE"),
        Keyword("GROCERY|KROGER|SAFEWAY|ALDI|WHOLE FOODS|DILLONS", TransactionCategory.FoodAndDrink, "FOOD_AND_DRINK_GROCERIES"),
        Keyword("RESTAURANT|PIZZA|BURGER|MCDONALD|TACO|DINER", TransactionCategory.FoodAndDrink, "FOOD_AND_DRINK_RESTAURANT"),
        Keyword("WAL-MART|WALMART|TARGET|AMAZON|AMZN|COSTCO", TransactionCategory.GeneralMerchandise, null),
        Keyword("RENT|ELECTRIC|WATER|INTERNET|UTILITY|UTILITIES", TransactionCategory.RentAndUtilities, null),
        Keyword("PHARMACY|CVS|WALGREENS|CLINIC|DENTAL|HOSPITAL", TransactionCategory.Medical, null),
        Keyword("AIRLINES|HOTEL|AIRBNB", TransactionCategory.Travel, null),
        Keyword("OVERDRAFT|ATM FEE|SERVICE FEE|MONTHLY FEE", TransactionCategory.BankFees, null),
    ];

    private static readonly HashSet<string> PlaidPrimaries = TransactionCategory.All
        .Where(category => category != TransactionCategory.Other)
        .ToHashSet();

    /// <param name="amount">Ledger sign: positive is money in. Keyword rules for transfers and pay depend on it.</param>
    public static Categorization Categorize(string? plaidPrimary, string? plaidDetailed, string? merchant, string description, decimal amount)
    {
        (plaidPrimary, plaidDetailed) = TransactionCategory.FromPlaid(plaidPrimary, plaidDetailed);
        if (plaidPrimary is not null && PlaidPrimaries.Contains(plaidPrimary))
        {
            // A detailed code from a different primary is not trusted.
            var subcategory = plaidDetailed is not null && plaidDetailed.StartsWith(plaidPrimary + "_", StringComparison.Ordinal)
                ? plaidDetailed
                : null;
            return new Categorization(plaidPrimary, subcategory);
        }

        var text = $"{merchant} {description}";
        foreach (var rule in Rules)
        {
            if (Applies(rule.Direction, amount) && rule.Pattern.IsMatch(text))
            {
                return new Categorization(rule.Category, rule.Subcategory);
            }
        }

        return new Categorization(TransactionCategory.Other, null);
    }

    private static bool Applies(Direction direction, decimal amount) => direction switch
    {
        Direction.In => amount > 0,
        Direction.Out => amount < 0,
        _ => true,
    };

    private static Rule Keyword(string alternatives, string category, string? subcategory, Direction direction = Direction.Any) =>
        new(new Regex($@"\b(?:{alternatives})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled), category, subcategory, direction);
}
