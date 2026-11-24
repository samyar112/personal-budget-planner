namespace backend.DTOs;

/// <summary>Handed to Plaid Link in the browser. Short-lived and only good for opening Link, so safe to return.</summary>
public sealed record PlaidLinkTokenResponse(string LinkToken);

/// <summary>The one-time token Plaid Link gives the browser after the user signs in to their bank.</summary>
public sealed record PlaidExchangeRequest(string PublicToken);

/// <summary>
/// What was linked. Deliberately no access token, item id or Plaid account id: the browser only
/// needs names to show, and anything more is a handle on the user's bank.
/// </summary>
public sealed record PlaidExchangeResponse(
    string? Institution,
    IReadOnlyList<LinkedAccountResponse> Accounts);

/// <summary>
/// One linked account. <c>Mask</c> is the last 4 digits for display; <c>Type</c> is Plaid's
/// (depository, credit, loan, investment, other), which the dashboard uses to pick out cards.
/// </summary>
public sealed record LinkedAccountResponse(
    Guid Id,
    string Name,
    string? Mask,
    string? Type,
    string? Subtype = null,
    decimal? CurrentBalance = null,
    DateTime? BalanceAsOf = null);

/// <summary>
/// One linked bank. <c>Id</c> is our own id, used to ask for a sync; Plaid's item id is never
/// returned. <c>LastSyncedAt</c> is null until the first sync commits.
/// </summary>
public sealed record PlaidItemResponse(
    Guid Id,
    string? Institution,
    DateTime? LastSyncedAt,
    IReadOnlyList<LinkedAccountResponse> Accounts);

/// <summary>Ledger rows inserted, updated and deleted by one sync.</summary>
public sealed record PlaidSyncResponse(int Added, int Modified, int Removed, DateTime LastSyncedAt);
