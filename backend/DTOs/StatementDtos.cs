namespace backend.DTOs;

/// <summary>
/// Result of one upload. <c>Skipped</c> counts lines already in the ledger from an earlier upload of the
/// same statement, so uploading twice is safe and says so.
/// </summary>
public sealed record StatementUploadResponse(Guid AccountId, int Imported, int Skipped);

/// <summary>
/// A statement account: rows from uploaded PDFs, kept out of every total until it is merged. <c>Mask</c>
/// is the last four digits printed on the statement, never the full number.
/// </summary>
public sealed record StatementAccountResponse(
    Guid Id,
    string Name,
    string? Mask,
    int TransactionCount,
    DateOnly? FirstDate,
    DateOnly? LastDate);

/// <summary>
/// One statement account's figures and rows, newest first. Income, expenses and net follow the same rules
/// as the main summary, computed over this account only.
/// </summary>
public sealed record StatementDetailResponse(
    StatementAccountResponse Account,
    decimal Income,
    decimal Expenses,
    decimal Net,
    IReadOnlyList<TransactionResponse> Transactions);
