using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. An ID token that does not verify returns 401, sets no cookie and creates no user.
 * 2. A blank ID token returns 400 without calling the verifier.
 * 3. A new Google user is created, email-verified, with no password, and gets the session cookies.
 * 4. The session cookie from Google sign-in reaches a verified-only endpoint.
 * 5. A returning Google user (same subject) signs in again without a duplicate row.
 * 6. A Google user whose Google email changed still signs in by subject, to the same account.
 * 7. A verified password account with the same email is linked to the Google subject, not duplicated.
 * 8. An UNVERIFIED password account with the same email is claimed: same user row, Google linked,
 *    email verified, password cleared, signed in.
 * 8a. Claiming revokes the unverified account's live sessions and unused verification/reset tokens.
 * 8b. After a claim the old sign-up password no longer logs in.
 * 9. An account already linked to a different Google subject is not re-linked (401).
 * 10. A deactivated user is refused with 403 and gets no cookies.
 * 11. Google sign-in skips the emailed login code, which a password login of a verified user requires.
 * 12. A missing Google client ID stops the app at startup.
 */
public sealed class GoogleAuthTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public GoogleAuthTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UnverifiableToken_ReturnsUnauthorized_AndCreatesNothing()
    {
        var before = (await UsersAsync(_ => true)).Count;

        var response = await PostGoogleAsync(_factory.CreateClient(), "forged-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, (await UsersAsync(_ => true)).Count);
    }

    [Fact]
    public async Task BlankToken_ReturnsBadRequest_WithoutCallingGoogle()
    {
        var before = _factory.Google.Verified.Count;

        var response = await PostGoogleAsync(_factory.CreateClient(), "  ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.Google.Verified.Count);
    }

    [Fact]
    public async Task NewUser_IsCreatedVerified_WithoutPassword_AndSignedIn()
    {
        var identity = NewIdentity();
        var token = _factory.Google.AddToken(identity);

        var response = await PostGoogleAsync(_factory.CreateClient(), token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(identity.Email, body!.Email);
        Assert.Equal(identity.Name, body.Name);
        AssertSessionCookies(response);

        var user = Assert.Single(await UsersAsync(candidate => candidate.Email == identity.Email));
        Assert.Equal(identity.Subject, user.GoogleSubject);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Null(user.PasswordHash);
        Assert.Equal(UserRole.User, user.Role);
    }

    [Fact]
    public async Task GoogleSession_ReachesVerifiedOnlyEndpoint()
    {
        var client = _factory.CreateClient();
        var token = _factory.Google.AddToken(NewIdentity());
        Assert.Equal(HttpStatusCode.OK, (await PostGoogleAsync(client, token)).StatusCode);

        var response = await client.GetAsync("/api/test/verified-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReturningUser_SignsInAgain_WithoutDuplicate()
    {
        var identity = NewIdentity();
        var token = _factory.Google.AddToken(identity);
        await PostGoogleAsync(_factory.CreateClient(), token);

        var second = await PostGoogleAsync(_factory.CreateClient(), token);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(await UsersAsync(user => user.GoogleSubject == identity.Subject));
    }

    [Fact]
    public async Task ChangedGoogleEmail_StillSignsInBySubject()
    {
        var identity = NewIdentity();
        await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));
        var renamed = identity with { Email = $"new.{Guid.NewGuid():N}@example.com" };

        var response = await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(renamed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(identity.Email, body!.Email);
        Assert.Single(await UsersAsync(user => user.GoogleSubject == identity.Subject));
    }

    [Fact]
    public async Task VerifiedPasswordAccount_IsLinked_NotDuplicated()
    {
        var identity = NewIdentity();
        await SeedPasswordUserAsync(identity.Email, verified: true);

        var response = await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = Assert.Single(await UsersAsync(candidate => candidate.Email == identity.Email));
        Assert.Equal(identity.Subject, user.GoogleSubject);
        Assert.NotNull(user.PasswordHash);
    }

    [Fact]
    public async Task UnverifiedPasswordAccount_IsClaimedByGoogle_KeepingTheSameRow()
    {
        var identity = NewIdentity();
        var originalId = await SeedPasswordUserAsync(identity.Email, verified: false);

        var response = await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSessionCookies(response);
        var user = Assert.Single(await UsersAsync(candidate => candidate.Email == identity.Email));
        Assert.Equal(originalId, user.Id);
        Assert.Equal(identity.Subject, user.GoogleSubject);
        Assert.NotNull(user.EmailVerifiedAt);
        // Whoever pre-registered chose that password, so it must not survive the claim.
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task Claiming_RevokesSessionsAndUnusedTokens_OfTheUnverifiedAccount()
    {
        var identity = NewIdentity();
        var userId = await SeedPasswordUserAsync(identity.Email, verified: false);
        await SeedLiveTokensAsync(userId);

        await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The claim's own new session is live; every token that existed before it is dead.
        var oldSession = await db.RefreshTokens.SingleAsync(token => token.TokenHash == "pre-claim-session");
        Assert.NotNull(oldSession.RevokedAt);
        Assert.All(
            await db.EmailVerificationTokens.Where(token => token.UserId == userId).ToListAsync(),
            token => Assert.NotNull(token.UsedAt));
        Assert.All(
            await db.PasswordResetTokens.Where(token => token.UserId == userId).ToListAsync(),
            token => Assert.NotNull(token.UsedAt));
    }

    [Fact]
    public async Task AfterClaim_TheOldSignUpPassword_NoLongerLogsIn()
    {
        var identity = NewIdentity();
        await SeedPasswordUserAsync(identity.Email, verified: false, password: "Password1!");
        await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));

        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(identity.Email, "Password1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task AccountLinkedToAnotherGoogleSubject_IsNotRelinked()
    {
        var first = NewIdentity();
        await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(first));
        var imposter = first with { Subject = $"sub-{Guid.NewGuid():N}" };

        var response = await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(imposter));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var user = Assert.Single(await UsersAsync(candidate => candidate.Email == first.Email));
        Assert.Equal(first.Subject, user.GoogleSubject);
    }

    [Fact]
    public async Task DeactivatedUser_IsForbidden_WithoutCookies()
    {
        var identity = NewIdentity();
        var token = _factory.Google.AddToken(identity);
        await PostGoogleAsync(_factory.CreateClient(), token);
        await SetActiveAsync(identity.Subject, false);

        var response = await PostGoogleAsync(_factory.CreateClient(), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task GoogleSignIn_SkipsTheEmailedLoginCode()
    {
        var identity = NewIdentity();
        var before = _factory.Emails.Sent.Count;

        var response = await PostGoogleAsync(_factory.CreateClient(), _factory.Google.AddToken(identity));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, _factory.Emails.Sent.Count);
    }

    [Fact]
    public void MissingClientId_StopsTheAppAtStartup()
    {
        using var factory = new NoGoogleClientIdWebApplicationFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private static GoogleIdentity NewIdentity() =>
        new($"sub-{Guid.NewGuid():N}", $"google.{Guid.NewGuid():N}@example.com", "Gina Google");

    private static Task<HttpResponseMessage> PostGoogleAsync(HttpClient client, string idToken) =>
        client.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequest(idToken));

    private static void AssertSessionCookies(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join("\n", cookies);
        Assert.Contains($"{AuthCookie.AccessTokenName}=", setCookie);
        Assert.Contains($"{AuthCookie.RefreshTokenName}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<User>> UsersAsync(Func<User, bool> filter)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.AsNoTracking().ToListAsync()).Where(filter).ToList();
    }

    private async Task<Guid> SeedPasswordUserAsync(string email, bool verified, string? password = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Email = email,
            Name = "Pat Password",
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
        };
        user.PasswordHash = password is null
            ? "not-a-real-hash"
            : scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedLiveTokensAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var later = DateTime.UtcNow.AddDays(1);
        db.RefreshTokens.Add(new RefreshToken { UserId = userId, TokenHash = "pre-claim-session", ExpiresAt = later });
        db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = userId, TokenHash = $"verify-{Guid.NewGuid():N}", ExpiresAt = later });
        db.PasswordResetTokens.Add(new PasswordResetToken { UserId = userId, TokenHash = $"reset-{Guid.NewGuid():N}", ExpiresAt = later });
        await db.SaveChangesAsync();
    }

    private async Task SetActiveAsync(string googleSubject, bool active)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.GoogleSubject == googleSubject);
        user.IsActive = active;
        await db.SaveChangesAsync();
    }
}

/// <summary>Factory with no Google client ID, which must stop the app at startup.</summary>
public sealed class NoGoogleClientIdWebApplicationFactory : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?> { ["Google:ClientId"] = "" };
}
