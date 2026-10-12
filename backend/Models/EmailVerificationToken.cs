namespace backend.Models;

/// <summary>
/// Emailed verification token (hash only). Single-use: <see cref="UsedAt"/> is stamped
/// when it is consumed or superseded by a newer token for the same user.
/// </summary>
public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 hash of the raw token in the emailed link.</summary>
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }
}
