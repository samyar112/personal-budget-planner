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

    /// <summary>True for money moved between the user's own accounts, which summaries should leave out.</summary>
    public static bool IsTransfer(string? category) => category is TransferIn or TransferOut;
}
