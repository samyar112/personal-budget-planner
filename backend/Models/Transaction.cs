namespace backend.Models;

/// <summary>One row of the ledger. Plaid, PDF upload and the dev seeder all write here.</summary>
public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public TransactionSource Source { get; set; }

    /// <summary>The producer's own id for this row, so a re-sync or re-upload can be deduplicated. Null for seed rows.</summary>
    public string? ExternalId { get; set; }

    public DateOnly Date { get; set; }
    public required string Description { get; set; }

    /// <summary>Signed: negative is money out, positive is money in.</summary>
    public decimal Amount { get; set; }

    /// <summary>Null until categorisation (P4b) assigns one.</summary>
    public string? Category { get; set; }
}
