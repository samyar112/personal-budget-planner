using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. A new user has 14 days left; the trial ends 14 days after sign-up.
 * 2. Part of a day left rounds up, so 13.2 days reads as 14.
 * 3. Exactly 14 days after sign-up the trial has expired with 0 days left, never negative.
 * 4. Admins have no trial: both values are null and they are never expired.
 * 5. /me returns the trial fields for a user, and nulls for an admin.
 * 6. AI tips for an expired user return 402 and the model is not called.
 * 7. An expired admin still gets tips.
 */
public sealed class TrialTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly AuthWebApplicationFactory _factory;

    public TrialTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Advisor.Reset();
    }

    [Fact]
    public void NewUser_HasFourteenDaysLeft()
    {
        var user = new User { Email = "a@example.com", Name = "A", DateEntered = Now };

        Assert.Equal(Now.AddDays(14), Trial.EndsAt(user));
        Assert.Equal(14, Trial.DaysRemaining(user, Now));
        Assert.False(Trial.IsExpired(user, Now));
    }

    [Fact]
    public void PartialDay_RoundsUp()
    {
        var user = new User { Email = "a@example.com", Name = "A", DateEntered = Now.AddDays(-0.8) };

        Assert.Equal(14, Trial.DaysRemaining(user, Now));
    }

    [Fact]
    public void FourteenDaysLater_IsExpired_WithZeroDaysLeft()
    {
        var user = new User { Email = "a@example.com", Name = "A", DateEntered = Now.AddDays(-14) };

        Assert.True(Trial.IsExpired(user, Now));
        Assert.Equal(0, Trial.DaysRemaining(user, Now));
        Assert.Equal(0, Trial.DaysRemaining(user, Now.AddDays(30)));
    }

    [Fact]
    public void Admin_HasNoTrial()
    {
        var admin = new User { Email = "a@example.com", Name = "A", DateEntered = Now.AddDays(-100), Role = UserRole.Admin };

        Assert.Null(Trial.EndsAt(admin));
        Assert.Null(Trial.DaysRemaining(admin, Now));
        Assert.False(Trial.IsExpired(admin, Now));
    }

    [Fact]
    public async Task Me_ReturnsTrialFields_ForUser_AndNullsForAdmin()
    {
        var userEmail = await SeedUserAsync(DateTime.UtcNow.AddDays(-3), UserRole.User);
        var adminEmail = await SeedUserAsync(DateTime.UtcNow.AddDays(-3), UserRole.Admin);

        var user = (await (await SignedInAsync(userEmail)).GetFromJsonAsync<MeResponse>("/api/auth/me"))!;
        var admin = (await (await SignedInAsync(adminEmail)).GetFromJsonAsync<MeResponse>("/api/auth/me"))!;

        Assert.Equal(11, user.TrialDaysRemaining);
        Assert.NotNull(user.TrialEndsAt);
        Assert.Null(admin.TrialDaysRemaining);
        Assert.Null(admin.TrialEndsAt);
    }

    [Fact]
    public async Task Tips_ForExpiredUser_ReturnsPaymentRequired_AndSendsNothing()
    {
        var email = await SeedUserAsync(DateTime.UtcNow.AddDays(-15), UserRole.User);
        var client = await SignedInAsync(email);
        var before = _factory.Advisor.Requests.Count;

        var response = await client.PostAsJsonAsync("/api/ai/tips", new GenerateTipsRequest(true));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal(before, _factory.Advisor.Requests.Count);
    }

    [Fact]
    public async Task Tips_ForExpiredAdmin_IsNotBlocked()
    {
        var email = await SeedUserAsync(DateTime.UtcNow.AddDays(-15), UserRole.Admin);
        var client = await SignedInAsync(email);

        var response = await client.PostAsJsonAsync("/api/ai/tips", new GenerateTipsRequest(true));

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    private async Task<string> SeedUserAsync(DateTime dateEntered, UserRole role)
    {
        var email = $"trial.{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Trial Tester",
            EmailVerifiedAt = DateTime.UtcNow,
            DateEntered = dateEntered,
            Role = role,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return email;
    }

    /// <summary>Password login, plus the emailed code when the account is verified.</summary>
    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
}
