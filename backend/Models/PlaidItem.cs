namespace backend.Models;

/// <summary>
/// One Plaid link: a user's login at one institution. Holds the access token, which is a
/// long-lived credential to that bank, so it is stored encrypted and never leaves the API.
/// </summary>
public class PlaidItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Plaid's id for the link. Not a secret, but not shown to the client either.</summary>
    public required string ItemId { get; set; }

    /// <summary>AES-GCM ciphertext from <c>AccessTokenProtector</c>, bound to <see cref="UserId"/>.</summary>
    public required string EncryptedAccessToken { get; set; }

    /// <summary>Plaid institution id, kept so a later slice can spot the same bank linked twice.</summary>
    public string? InstitutionId { get; set; }

    public string? InstitutionName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
