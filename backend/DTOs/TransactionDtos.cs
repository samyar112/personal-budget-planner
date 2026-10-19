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
    string? Category);

public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items,
    int Page,
    int PageSize,
    int Total);
