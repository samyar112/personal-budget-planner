using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public sealed record IssuedRefreshToken(string RawToken, DateTime ExpiresAt, Guid TokenId);

public sealed record RefreshOutcome(
    TokenResult AccessToken,
    /// <summary>Null when the presented token was already rotated but still within grace — access only.</summary>
    IssuedRefreshToken? NewRefreshToken);

/// <summary>
/// Issues, rotates, and revokes opaque refresh tokens stored as hashes.
/// </summary>
public sealed class RefreshTokenService(
    AppDbContext db,
    TokenProvider tokenProvider,
    IOptions<JwtOptions> jwtOptions)
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public async Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var rawToken = CreateRawToken();
        var expiresAt = DateTime.UtcNow.AddDays(_options.RefreshExpiresDays);
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = expiresAt,
        };

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(rawToken, expiresAt, entity.Id);
    }

    /// <summary>
    /// Rotates an active refresh token, or during grace returns a new access token without rotating again.
    /// </summary>
    public async Task<RefreshOutcome?> RefreshAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var hash = HashToken(rawToken);
        var existing = await db.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (existing is null || existing.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        var access = tokenProvider.Create(existing.User);

        // Concurrent tab: token already rotated but still inside the grace window.
        if (existing.RevokedAt is not null)
        {
            if (existing.GraceExpiresAt is { } grace && grace > DateTime.UtcNow)
            {
                return new RefreshOutcome(access, NewRefreshToken: null);
            }

            return null;
        }

        var replacement = await IssueAsync(existing.User, cancellationToken);

        existing.RevokedAt = DateTime.UtcNow;
        existing.GraceExpiresAt = DateTime.UtcNow.AddSeconds(_options.RefreshGraceSeconds);
        existing.ReplacedByTokenId = replacement.TokenId;
        await db.SaveChangesAsync(cancellationToken);

        return new RefreshOutcome(access, replacement);
    }

    public async Task RevokeAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var hash = HashToken(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null)
        {
            return;
        }

        existing.RevokedAt = DateTime.UtcNow;
        existing.GraceExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateRawToken()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    internal static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
