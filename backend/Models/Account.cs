namespace backend.Models;

/// <summary>A bank account whose transactions land in the ledger.</summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Name { get; set; }

    /// <summary>Institution name for display, for example "Sandbox Bank". Never an account number.</summary>
    public string? Institution { get; set; }

    /// <summary>Set when Plaid produced this account; null for seed or PDF accounts.</summary>
    public Guid? PlaidItemId { get; set; }
    public PlaidItem? PlaidItem { get; set; }

    /// <summary>Plaid's account id, used to match the account again on a later exchange or sync.</summary>
    public string? PlaidAccountId { get; set; }

    /// <summary>
    /// Last 4 digits as Plaid reports them, for telling cards apart on screen. Never the full number.
    /// Identifying data: #34 must strip it before any AI call.
    /// </summary>
    public string? Mask { get; set; }

    /// <summary>Plaid account type: depository, credit, loan, investment or other. Null for non-Plaid accounts.</summary>
    public string? Type { get; set; }

    /// <summary>Plaid account subtype, for example checking, savings or credit card. Null until the next sync.</summary>
    public string? Subtype { get; set; }

    /// <summary>
    /// Balance as of <see cref="BalanceAsOf"/>, from the last link or sync. For credit accounts it is the
    /// amount owed. Financial data: must stay out of any AI payload.
    /// </summary>
    public decimal? CurrentBalance { get; set; }

    /// <summary>When <see cref="CurrentBalance"/> was read from Plaid. Null when Plaid reported no balance.</summary>
    public DateTime? BalanceAsOf { get; set; }

    /// <summary>
    /// The card's credit limit from the last link or sync, for card utilisation in the health score. Null for
    /// non-credit accounts and when Plaid reports none. Financial data: must stay out of any AI payload.
    /// </summary>
    public decimal? CreditLimit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
