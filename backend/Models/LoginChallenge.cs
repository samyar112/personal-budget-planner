namespace backend.Models;

/// <summary>
/// Pending second step of a password login. Holds only a keyed hash of the emailed code.
/// Single-use: <see cref="UsedAt"/> is stamped when the code is consumed, superseded by a
/// newer login, or the attempt cap is reached.
/// </summary>
public class LoginChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Base64 HMAC-SHA256 of the code, keyed with the server secret and bound to <see cref="Id"/>.</summary>
    public required string CodeHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Wrong codes so far. Never reset by a resend, so resending cannot buy more guesses.</summary>
    public int Attempts { get; set; }

    /// <summary>When the current code was emailed. Drives the resend cooldown.</summary>
    public DateTime? LastSentAt { get; set; }

    public DateTime? UsedAt { get; set; }
}
