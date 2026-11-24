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
/// One account's cash flow for a date range. <c>Income</c> is money in and <c>Expenses</c> is money out
/// as a positive number, so neither is ever negative. <c>Net</c> is income minus expenses.
/// </summary>
public sealed record AccountSummaryResponse(Guid AccountId, decimal Income, decimal Expenses, decimal Net);

/// <summary>
/// Totals for a date range, with transfers and credit card payments left out. The totals are the sum
/// of <c>Accounts</c>, so the two always reconcile. <c>Savings</c> is income minus expenses.
/// </summary>
public sealed record TransactionSummaryResponse(
    decimal Income,
    decimal Expenses,
    decimal Savings,
    IReadOnlyList<AccountSummaryResponse> Accounts);
