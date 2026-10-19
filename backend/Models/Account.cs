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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
