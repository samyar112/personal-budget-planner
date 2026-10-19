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
 * 1. Listing users without a session returns 401.
 * 2. A regular user gets 403 from every admin endpoint, and the target is untouched.
 * 3. An admin gets the list, and the body carries no hash, token or Google subject.
 * 4. verified=false returns only unverified accounts; verified=true only verified ones.
 * 5. Paging returns the requested page size and the total.
 * 6. An admin demoted in the database is refused at once, though the JWT still says Admin.
 * 7. A deactivated admin is refused at once, though the JWT is still valid.
 * 8. /me reports the role.
 * 9. Deactivating blocks password login with a clear message, for verified and unverified users.
 * 10. A deactivated user with the wrong password still gets the generic 401, so nothing leaks.
 * 11. Deactivating ends existing sessions: the refresh cookie stops working.
 * 12. A code challenge issued before deactivation does not sign the user in.
 * 13. Deactivate writes one audit entry; repeating it changes nothing and writes no second one.
 * 14. An admin cannot deactivate themselves or another admin, and no audit entry is written.
 * 15. An unknown account returns 404.
 * 16. Activating restores login and writes an audit entry.
 * 17. The seeder promotes an existing verified account, and nothing else.
 * 18. The seeder never creates an account and ignores unverified ones.
 * 19. A malformed Admin:Email stops the app at startup.
 */
public sealed class AdminEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public AdminEndpointTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_ForRegularUser_ReturnForbiddenAndChangeNothing()
    {
        var (_, userEmail) = await SeedAsync(_factory);
        var (targetId, _) = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, userEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/admin/users/{targetId}/deactivate", null)).StatusCode);

        Assert.True((await FindAsync(_factory, targetId)).IsActive);
    }

    [Fact]
    public async Task List_ForAdmin_ReturnsUsersWithoutSecrets()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (userId, userEmail) = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, adminEmail);

        var response = await client.GetAsync("/api/admin/users?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("googleSubject", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);

        var list = await response.Content.ReadFromJsonAsync<AdminUserListResponse>();
        var row = Assert.Single(list!.Items, item => item.Id == userId);
        Assert.Equal(userEmail, row.Email);
        Assert.Equal("User", row.Role);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task List_VerifiedFilter_SplitsVerifiedFromUnverified()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (verifiedId, _) = await SeedAsync(_factory);
        var (unverifiedId, _) = await SeedAsync(_factory, verified: false);
        var client = await SignedInAsync(_factory, adminEmail);

        var unverified = await client.GetFromJsonAsync<AdminUserListResponse>(
            "/api/admin/users?verified=false&pageSize=100");
        var verified = await client.GetFromJsonAsync<AdminUserListResponse>(
            "/api/admin/users?verified=true&pageSize=100");

        Assert.All(unverified!.Items, item => Assert.False(item.EmailVerified));
        Assert.Contains(unverified.Items, item => item.Id == unverifiedId);
        Assert.DoesNotContain(unverified.Items, item => item.Id == verifiedId);
        Assert.All(verified!.Items, item => Assert.True(item.EmailVerified));
        Assert.Contains(verified.Items, item => item.Id == verifiedId);
    }

    [Fact]
    public async Task List_Paging_ReturnsRequestedPageSizeAndTotal()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (_, adminEmail) = await SeedAsync(factory, UserRole.Admin);
        await SeedAsync(factory);
        await SeedAsync(factory);
        var client = await SignedInAsync(factory, adminEmail);

        var first = await client.GetFromJsonAsync<AdminUserListResponse>("/api/admin/users?page=1&pageSize=2");
        var second = await client.GetFromJsonAsync<AdminUserListResponse>("/api/admin/users?page=2&pageSize=2");

        Assert.Equal(2, first!.Items.Count);
        Assert.Equal(3, first.Total);
        Assert.Single(second!.Items);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
    }

    [Fact]
    public async Task List_ForAdminDemotedInDatabase_ReturnsForbiddenDespiteValidJwt()
    {
        var (adminId, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var client = await SignedInAsync(_factory, adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);

        await UpdateAsync(_factory, adminId, user => user.Role = UserRole.User);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task List_ForDeactivatedAdmin_ReturnsForbiddenDespiteValidJwt()
    {
        var (adminId, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var client = await SignedInAsync(_factory, adminEmail);

        await UpdateAsync(_factory, adminId, user => user.IsActive = false);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Me_ReportsTheRole()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (_, userEmail) = await SeedAsync(_factory);

        var admin = await (await SignedInAsync(_factory, adminEmail)).GetFromJsonAsync<MeResponse>("/api/auth/me");
        var user = await (await SignedInAsync(_factory, userEmail)).GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal("Admin", admin!.Role);
        Assert.Equal("User", user!.Role);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_ForDeactivatedUser_IsRefusedWithAMessage(bool verified)
    {
        var (id, email) = await SeedAsync(_factory, verified: verified, active: false);

        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains("deactivated", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await FindAsync(_factory, id)).FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_ForDeactivatedUserWithWrongPassword_StaysGenericUnauthorized()
    {
        var (_, email) = await SeedAsync(_factory, active: false);

        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong-Password-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("deactivated", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deactivate_EndsExistingSessions()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (userId, userEmail) = await SeedAsync(_factory);
        var userClient = await SignedInAsync(_factory, userEmail);
        Assert.Equal(HttpStatusCode.OK, (await userClient.PostAsync("/api/auth/refresh", null)).StatusCode);
        var adminClient = await SignedInAsync(_factory, adminEmail);

        var response = await adminClient.PostAsync($"/api/admin/users/{userId}/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False((await FindAsync(_factory, userId)).IsActive);
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.PostAsync("/api/auth/refresh", null)).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(await db.RefreshTokens.Where(token => token.UserId == userId).ToListAsync(),
            token => token.RevokedAt is null);
    }

    [Fact]
    public async Task VerifyCode_ForUserDeactivatedAfterTheChallenge_DoesNotSignIn()
    {
        var (userId, userEmail) = await SeedAsync(_factory);
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userEmail, Password));
        var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
        var code = CapturingEmailSender.CodeFrom(_factory.Emails.LastTo(userEmail));
        await UpdateAsync(_factory, userId, user => user.IsActive = false);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Deactivate_WritesOneAuditEntry_AndIsIdempotent()
    {
        var (adminId, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (userId, _) = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, adminEmail);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/admin/users/{userId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/admin/users/{userId}/deactivate", null)).StatusCode);

        var entries = await AuditFor(_factory, userId);
        var entry = Assert.Single(entries);
        Assert.Equal(adminId, entry.AdminId);
        Assert.Equal(AdminAction.Deactivate, entry.Action);
    }

    [Fact]
    public async Task Deactivate_RefusesSelfAndOtherAdmins_AndWritesNoAudit()
    {
        var (adminId, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (otherAdminId, _) = await SeedAsync(_factory, UserRole.Admin);
        var client = await SignedInAsync(_factory, adminEmail);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/users/{adminId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/users/{otherAdminId}/deactivate", null)).StatusCode);

        Assert.True((await FindAsync(_factory, adminId)).IsActive);
        Assert.True((await FindAsync(_factory, otherAdminId)).IsActive);
        Assert.Empty(await AuditFor(_factory, adminId));
        Assert.Empty(await AuditFor(_factory, otherAdminId));
    }

    [Fact]
    public async Task Deactivate_UnknownAccount_ReturnsNotFound()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var client = await SignedInAsync(_factory, adminEmail);

        var response = await client.PostAsync($"/api/admin/users/{Guid.NewGuid()}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Activate_RestoresLogin_AndWritesAuditEntry()
    {
        var (_, adminEmail) = await SeedAsync(_factory, UserRole.Admin);
        var (userId, userEmail) = await SeedAsync(_factory, active: false);
        var client = await SignedInAsync(_factory, adminEmail);

        var response = await client.PostAsync($"/api/admin/users/{userId}/activate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await FindAsync(_factory, userId)).IsActive);
        Assert.Equal(AdminAction.Activate, Assert.Single(await AuditFor(_factory, userId)).Action);
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(userEmail, Password));
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
    }

    [Fact]
    public async Task Seeder_PromotesAnExistingVerifiedAccount_AndNothingElse()
    {
        var (targetId, targetEmail) = await SeedAsync(_factory);
        var (bystanderId, _) = await SeedAsync(_factory);
        await using var factory = new AdminSeedWebApplicationFactory(targetEmail);
        await CopyUserAsync(_factory, factory, targetId);
        await CopyUserAsync(_factory, factory, bystanderId);

        await SeedAdminAsync(factory);

        Assert.Equal(UserRole.Admin, (await FindAsync(factory, targetId)).Role);
        Assert.Equal(UserRole.User, (await FindAsync(factory, bystanderId)).Role);
    }

    [Fact]
    public async Task Seeder_NeverCreatesAnAccount_AndIgnoresUnverifiedOnes()
    {
        var email = $"admin.{Guid.NewGuid():N}@example.com";
        await using var factory = new AdminSeedWebApplicationFactory(email);

        await SeedAdminAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Users.AnyAsync(user => user.Email == email));
        }

        var (id, _) = await SeedAsync(factory, verified: false, email: email);
        await SeedAdminAsync(factory);

        Assert.Equal(UserRole.User, (await FindAsync(factory, id)).Role);
    }

    [Fact]
    public async Task MalformedAdminEmail_StopsTheAppAtStartup()
    {
        await using var factory = new AdminSeedWebApplicationFactory("not-an-email");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    // --- helpers ---

    private static async Task<(Guid Id, string Email)> SeedAsync(
        AuthWebApplicationFactory factory,
        UserRole role = UserRole.User,
        bool verified = true,
        bool active = true,
        string? email = null)
    {
        email ??= $"user.{Guid.NewGuid():N}@example.com";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Test User",
            Role = role,
            IsActive = active,
            EmailVerifiedAt = verified ? DateTime.UtcNow : null,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    /// <summary>Password login, plus the emailed code when the account is verified.</summary>
    private static async Task<HttpClient> SignedInAsync(AuthWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        if (login.StatusCode == HttpStatusCode.Accepted)
        {
            var challenge = await login.Content.ReadFromJsonAsync<LoginChallengeResponse>();
            var code = CapturingEmailSender.CodeFrom(factory.Emails.LastTo(email));
            login = await client.PostAsJsonAsync(
                "/api/auth/login/verify-code", new VerifyLoginCodeRequest(challenge!.ChallengeId, code));
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<User> FindAsync(AuthWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.AsNoTracking().SingleAsync(user => user.Id == id);
    }

    private static async Task UpdateAsync(AuthWebApplicationFactory factory, Guid id, Action<User> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Users.SingleAsync(user => user.Id == id));
        await db.SaveChangesAsync();
    }

    private static async Task<List<AdminAuditEntry>> AuditFor(AuthWebApplicationFactory factory, Guid targetId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .AdminAuditEntries.Where(entry => entry.TargetUserId == targetId).ToListAsync();
    }

    private static async Task CopyUserAsync(AuthWebApplicationFactory from, AuthWebApplicationFactory to, Guid id)
    {
        var source = await FindAsync(from, id);
        using var scope = to.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(source);
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(CancellationToken.None);
    }
}

/// <summary>Factory configured with the first-admin email.</summary>
public sealed class AdminSeedWebApplicationFactory(string adminEmail) : AuthWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraConfig { get; } =
        new Dictionary<string, string?> { ["Admin:Email"] = adminEmail };
}
