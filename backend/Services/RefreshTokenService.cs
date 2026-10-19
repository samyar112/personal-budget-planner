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
    private const int MaxConflictRetries = 3;

    private readonly JwtOptions _options = jwtOptions.Value;

    public async Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var (entity, rawToken) = Create(user, Guid.NewGuid());
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(rawToken, entity.ExpiresAt, entity.Id);
    }

    /// <summary>
    /// Rotates an active refresh token, or during grace returns a new access token without rotating again.
    /// A rotated token replayed after its grace window means two parties hold the same lineage, so the
    /// whole family is revoked. RevokedAt is a concurrency token: of two parallel rotations only one
    /// saves, and the loser comes back through the grace path instead of forking a second live token.
    /// </summary>
    public async Task<RefreshOutcome?> RefreshAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        for (var attempt = 0; attempt < MaxConflictRetries; attempt++)
        {
            try
            {
                return await TryRefreshAsync(rawToken, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return null;
    }

    private async Task<RefreshOutcome?> TryRefreshAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = HashToken(rawToken);
        var existing = await db.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (existing is null || existing.ExpiresAt <= DateTime.UtcNow || !existing.User.IsActive)
        {
            return null;
        }

        var access = tokenProvider.Create(existing.User);

        if (existing.RevokedAt is not null)
        {
            // Concurrent tab: token already rotated but still inside the grace window.
            if (existing.GraceExpiresAt is { } grace && grace > DateTime.UtcNow)
            {
                return new RefreshOutcome(access, NewRefreshToken: null);
            }

            // Rotated and long gone: a copy of it was kept. A logout-revoked token has no successor
            // and is just a stale tab, so only a rotated one burns the family.
            if (existing.ReplacedByTokenId is not null)
            {
                await RevokeFamilyAsync(existing.FamilyId, cancellationToken);
            }

            return null;
        }

        // Retire the old token first: the concurrency check fails here, before any replacement exists,
        // so a lost race cannot leave a second live token. If the process dies between the two saves
        // the session is simply gone and the user logs in again.
        var (replacement, replacementRaw) = Create(existing.User, existing.FamilyId);
        existing.RevokedAt = DateTime.UtcNow;
        existing.GraceExpiresAt = DateTime.UtcNow.AddSeconds(_options.RefreshGraceSeconds);
        existing.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(cancellationToken);

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);

        return new RefreshOutcome(
            access,
            new IssuedRefreshToken(replacementRaw, replacement.ExpiresAt, replacement.Id));
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var live = await db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in live)
        {
            token.RevokedAt = now;
            token.GraceExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var hash = HashToken(rawToken);
        for (var attempt = 0; attempt < MaxConflictRetries; attempt++)
        {
            var existing = await db.RefreshTokens
                .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

            if (existing is null)
            {
                return;
            }

            try
            {
                // Logging out must end the whole lineage, including a rotation that raced it.
                await RevokeFamilyAsync(existing.FamilyId, cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private (RefreshToken Entity, string RawToken) Create(User user, Guid familyId)
    {
        var rawToken = CreateRawToken();
        var entity = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshExpiresDays),
        };

        return (entity, rawToken);
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
