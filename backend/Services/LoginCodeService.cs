using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.Extensions;
using backend.Models;
using backend.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public enum LoginCodeStatus
{
    Success,
    Invalid,
    Locked,
    Cooldown,
}

/// <summary>Outcome of a verify or resend call. <see cref="User"/> is set only on a verified code.</summary>
public sealed record LoginCodeResult(LoginCodeStatus Status, User? User = null, DateTime? ResendAvailableAt = null);

public sealed record IssuedLoginChallenge(Guid ChallengeId, DateTime ResendAvailableAt);

/// <summary>
/// Second step of password login: emails a 6-digit code and consumes it. The code is stored only
/// as an HMAC keyed with the server secret and bound to the challenge id, so a leaked table cannot
/// be brute-forced offline and a code never opens a different challenge.
/// </summary>
public sealed class LoginCodeService(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<AuthSecurityOptions> securityOptions,
    IOptions<JwtOptions> jwtOptions,
    ILogger<LoginCodeService> logger)
{
    private const int MaxConflictRetries = 3;

    /// <summary>
    /// Starts a challenge, superseding any unused ones, and emails the code. A mail failure is
    /// logged, not thrown: the challenge exists and the user can resend (no cooldown is recorded
    /// for a code that was never sent).
    /// </summary>
    public async Task<IssuedLoginChallenge> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var pending = await db.LoginChallenges
            .Where(challenge => challenge.UserId == user.Id && challenge.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in pending)
        {
            challenge.UsedAt = now;
        }

        var created = new LoginChallenge { UserId = user.Id, CodeHash = string.Empty };
        db.LoginChallenges.Add(created);
        await SendNewCodeAsync(created, user, now, cancellationToken);

        return new IssuedLoginChallenge(created.Id, ResendAvailableAt(created, now));
    }

    /// <summary>
    /// Checks a code. Attempts and use are concurrency tokens, so parallel guesses cannot all be
    /// counted against the same stale value: the loser reloads and is judged on the current row.
    /// </summary>
    public async Task<LoginCodeResult> VerifyAsync(
        Guid challengeId,
        string? code,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxConflictRetries; attempt++)
        {
            try
            {
                return await TryVerifyAsync(challengeId, code, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return new LoginCodeResult(LoginCodeStatus.Invalid);
    }

    private async Task<LoginCodeResult> TryVerifyAsync(
        Guid challengeId,
        string? code,
        CancellationToken cancellationToken)
    {
        var security = securityOptions.Value;
        var now = DateTime.UtcNow;

        var challenge = await FindUsableAsync(challengeId, now, cancellationToken);
        if (challenge is null)
        {
            return new LoginCodeResult(LoginCodeStatus.Invalid);
        }

        var user = challenge.User;
        if (AuthSecurityExtensions.IsLockedOut(user, now))
        {
            return new LoginCodeResult(LoginCodeStatus.Locked);
        }

        var candidate = Hash(challenge.Id, code ?? string.Empty);
        var matches = CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(challenge.CodeHash),
            Convert.FromBase64String(candidate));

        if (!matches)
        {
            challenge.Attempts += 1;
            if (challenge.Attempts >= security.LoginCodeMaxAttempts)
            {
                challenge.UsedAt = now;
            }

            // Guessing codes feeds the same lockout as guessing passwords.
            AuthSecurityExtensions.RegisterFailedLogin(user, security);
            await db.SaveChangesAsync(cancellationToken);

            return new LoginCodeResult(
                AuthSecurityExtensions.IsLockedOut(user, now) ? LoginCodeStatus.Locked : LoginCodeStatus.Invalid);
        }

        challenge.UsedAt = now;
        AuthSecurityExtensions.ClearLoginFailures(user);
        await db.SaveChangesAsync(cancellationToken);
        return new LoginCodeResult(LoginCodeStatus.Success, user);
    }

    /// <summary>Emails a fresh code for the same challenge, replacing the old one.</summary>
    public async Task<LoginCodeResult> ResendAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var challenge = await FindUsableAsync(challengeId, now, cancellationToken);
        if (challenge is null)
        {
            return new LoginCodeResult(LoginCodeStatus.Invalid);
        }

        var availableAt = ResendAvailableAt(challenge, now);
        if (availableAt > now)
        {
            return new LoginCodeResult(LoginCodeStatus.Cooldown, ResendAvailableAt: availableAt);
        }

        try
        {
            await SendNewCodeAsync(challenge, challenge.User, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The challenge was spent or guessed against while this resend was in flight.
            return new LoginCodeResult(LoginCodeStatus.Invalid);
        }

        return new LoginCodeResult(
            LoginCodeStatus.Success,
            ResendAvailableAt: ResendAvailableAt(challenge, now));
    }

    private Task<LoginChallenge?> FindUsableAsync(Guid challengeId, DateTime now, CancellationToken cancellationToken) =>
        db.LoginChallenges
            .Include(challenge => challenge.User)
            .FirstOrDefaultAsync(
                challenge => challenge.Id == challengeId
                    && challenge.UsedAt == null
                    && challenge.ExpiresAt > now
                    && challenge.Attempts < securityOptions.Value.LoginCodeMaxAttempts,
                cancellationToken);

    private async Task SendNewCodeAsync(
        LoginChallenge challenge,
        User user,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lifetime = TimeSpan.FromMinutes(securityOptions.Value.LoginCodeMinutes);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        challenge.CodeHash = Hash(challenge.Id, code);
        challenge.ExpiresAt = now + lifetime;
        challenge.LastSentAt = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(EmailTemplates.LoginCode(user.Email, code, lifetime), cancellationToken);
            challenge.LastSentAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Login code email failed for user {UserId}.", user.Id);
        }
    }

    private DateTime ResendAvailableAt(LoginChallenge challenge, DateTime now) =>
        // A code that never went out (mail failure) carries no cooldown.
        challenge.LastSentAt?.AddSeconds(securityOptions.Value.LoginCodeResendCooldownSeconds) ?? now;

    private string Hash(Guid challengeId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtOptions.Value.Key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));
    }
}
