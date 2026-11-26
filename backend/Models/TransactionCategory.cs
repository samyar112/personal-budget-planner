using System.Linq.Expressions;

namespace backend.Models;

/// <summary>
/// The one category set for the ledger. It adopts Plaid's personal_finance_category taxonomy (v1):
/// <see cref="Transaction.Category"/> holds a primary code from <see cref="All"/>, and
/// <see cref="Transaction.Subcategory"/> holds the matching Plaid detailed code, which always starts
/// with its primary plus an underscore. Producers other than Plaid (PDF upload, keyword rules) emit
/// the same codes. The chart (P7) and recurring detection (P8) read this set.
/// </summary>
public static class TransactionCategory
{
    public const string Income = "INCOME";
    public const string TransferIn = "TRANSFER_IN";
    public const string TransferOut = "TRANSFER_OUT";
    public const string LoanPayments = "LOAN_PAYMENTS";
    public const string BankFees = "BANK_FEES";
    public const string Entertainment = "ENTERTAINMENT";
    public const string FoodAndDrink = "FOOD_AND_DRINK";
    public const string GeneralMerchandise = "GENERAL_MERCHANDISE";
    public const string HomeImprovement = "HOME_IMPROVEMENT";
    public const string Medical = "MEDICAL";
    public const string PersonalCare = "PERSONAL_CARE";
    public const string GeneralServices = "GENERAL_SERVICES";
    public const string GovernmentAndNonProfit = "GOVERNMENT_AND_NON_PROFIT";
    public const string Transportation = "TRANSPORTATION";
    public const string Travel = "TRAVEL";
    public const string RentAndUtilities = "RENT_AND_UTILITIES";

    /// <summary>Ours, not Plaid's: the default when nothing matches.</summary>
    public const string Other = "OTHER";

    public static readonly IReadOnlyList<string> All =
    [
        Income, TransferIn, TransferOut, LoanPayments, BankFees, Entertainment, FoodAndDrink,
        GeneralMerchandise, HomeImprovement, Medical, PersonalCare, GeneralServices,
        GovernmentAndNonProfit, Transportation, Travel, RentAndUtilities, Other,
    ];

    /// <summary>
    /// Plaid's subcategory for paying a credit card. It shows up twice, as money out of the paying account
    /// and as money into the card, so summaries leave it out; the card's own purchases are already counted.
    /// </summary>
    public const string CreditCardPayment = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";

    /// <summary>Plaid's subcategory for cash taken out (ATM, teller). Money gone, so summaries count it as spending.</summary>
    public const string CashWithdrawal = "TRANSFER_OUT_WITHDRAWAL";

    /// <summary>
    /// Plaid's subcategory for money sent to someone else, such as rent paid by Zelle. Also spending.
    /// </summary>
    public const string OtherTransferOut = "TRANSFER_OUT_OTHER_TRANSFER_OUT";

    /// <summary>True for the transfer primaries. Not every transfer is internal; see <see cref="CountedInSummaries"/>.</summary>
    public static bool IsTransfer(string? category) => category is TransferIn or TransferOut;

    /// <summary>
    /// The one rule for what income, expense and net cash flow figures count. Left out: every transfer in,
    /// transfers out between the user's own accounts, and credit card payments. Cash withdrawals and
    /// transfers to other people are spending, so they stay. Without pairing both legs of a transfer this
    /// is a heuristic: an own-account move that Plaid labels "other transfer out" still counts.
    /// An expression, so it runs in the database. Rows with no category count.
    /// </summary>
    public static readonly Expression<Func<Transaction, bool>> CountedInSummaries = transaction =>
        transaction.Category != TransferIn
        && (transaction.Category != TransferOut
            || transaction.Subcategory == CashWithdrawal
            || transaction.Subcategory == OtherTransferOut)
        && transaction.Subcategory != CreditCardPayment;

    /// <summary>
    /// A counted row that is spending: anything not income. Income is the INCOME category, or money in on a
    /// row with no category; the summary's income sum uses the same rule. Refunds are spending rows with a
    /// positive amount, so they net against their own category. An expression, so it runs in the database.
    /// </summary>
    public static readonly Expression<Func<Transaction, bool>> IsSpending = transaction =>
        transaction.Category != Income && (transaction.Category != null || transaction.Amount <= 0);

    /// <summary>
    /// Plaid's v2 taxonomy, mapped onto the v1 set stored here. Teams created after December 2025 receive
    /// v2. Keyed by detailed code; <see cref="FromPlaid"/> also maps a v2-only primary with no detailed code.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PlaidV2Detailed = new Dictionary<string, string>
    {
        ["INCOME_SALARY"] = "INCOME_WAGES",
    };

    private const string LoanDisbursements = "LOAN_DISBURSEMENTS";
    private const string LoanProceeds = "TRANSFER_IN_CASH_ADVANCES_AND_LOANS";

    /// <summary>
    /// Translates a Plaid code pair from either taxonomy version into the v1 set. A loan or cash advance
    /// becomes a transfer in, so borrowed money is never counted as income.
    /// </summary>
    public static (string? Primary, string? Detailed) FromPlaid(string? primary, string? detailed)
    {
        if (primary == LoanDisbursements)
        {
            return (TransferIn, LoanProceeds);
        }

        return (primary, detailed is not null && PlaidV2Detailed.TryGetValue(detailed, out var v1) ? v1 : detailed);
    }
}
