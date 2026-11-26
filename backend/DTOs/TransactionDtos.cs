namespace backend.DTOs;

/// <summary>
/// One ledger row. <c>Amount</c> is signed: negative is money out. No user id is returned; the
/// caller only ever sees their own rows.
/// </summary>
public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    string Source,
    DateOnly Date,
    string Description,
    decimal Amount,
    string? Category,
    string? Subcategory = null);

public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// One account's cash flow for a date range. <c>Income</c> is INCOME rows; <c>Expenses</c> is money out as
/// a positive number, reduced by refunds, so it can go negative. <c>Net</c> is income minus expenses.
/// </summary>
public sealed record AccountSummaryResponse(Guid AccountId, decimal Income, decimal Expenses, decimal Net);

/// <summary>
/// Totals for a date range, with transfers and credit card payments left out. The totals are the sum
/// of <c>Accounts</c>, so the two always reconcile. <c>NetCashFlow</c> is income minus expenses: money left over, not money moved into savings.
/// </summary>
public sealed record TransactionSummaryResponse(
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    IReadOnlyList<AccountSummaryResponse> Accounts);

/// <summary>
/// Spending in one category: money out minus refunds in that category, as a positive number. Zero or
/// negative when refunds outweigh purchases; the client decides whether to draw those.
/// </summary>
public sealed record CategorySpendingResponse(string Category, decimal Amount);

/// <summary>
/// Spending by category, largest first. <c>Total</c> is the sum of every category, so it equals the
/// summary's <c>Expenses</c> for the same range and account.
/// </summary>
public sealed record SpendingByCategoryResponse(decimal Total, IReadOnlyList<CategorySpendingResponse> Categories);

/// <summary>One calendar month of cash flow, by the same rules as the summary. Empty months are zeros.</summary>
public sealed record MonthlyCashFlowResponse(int Year, int Month, decimal Income, decimal Expenses, decimal NetCashFlow);

/// <summary>Every month in the requested range, oldest first, including months with no rows.</summary>
public sealed record MonthlyCashFlowListResponse(IReadOnlyList<MonthlyCashFlowResponse> Months);

/// <summary>
/// Spending with one payee, keyed by the stored description. <c>Amount</c> is net of refunds from the
/// same payee; <c>Count</c> is the number of rows behind it.
/// </summary>
public sealed record MerchantSpendingResponse(string Merchant, decimal Amount, int Count);

/// <summary>The largest payees by net spending, largest first. Payees with nothing left after refunds are left out.</summary>
public sealed record TopMerchantsResponse(IReadOnlyList<MerchantSpendingResponse> Merchants);
