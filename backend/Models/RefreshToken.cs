namespace backend.Models;

/// <summary>
/// Persisted opaque refresh token (hash only). Supports rotation with a short grace window.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 hash of the raw token sent in the HttpOnly cookie.</summary>
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>While set and in the future, a just-rotated token may still mint a new access JWT.</summary>
    public DateTime? GraceExpiresAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}
