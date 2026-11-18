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

public sealed record LinkedAccountResponse(Guid Id, string Name);
