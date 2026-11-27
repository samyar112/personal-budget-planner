using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/*
 * Test cases:
 * 1. An unhandled exception returns a 500 ProblemDetails with a traceId and no exception message or stack trace.
 * 2. The exception is stored with method, path, status, type, message, stack trace and the same traceId.
 * 3. Only the path is stored: a token in the query string appears in no column.
 * 4. A signed-in user's id is recorded; an anonymous request records none.
 * 5. Handled 4xx responses (bad login, unknown route) write no row.
 * 6. If the log write fails, the client still gets the 500 ProblemDetails.
 * 7. Writing a new row purges rows older than 30 days and keeps newer ones.
 * 8. GET /api/admin/errors: 401 without a session, 403 for a regular user.
 * 9. An admin gets the log newest first, paged, with the total.
 */
public sealed class ErrorLogTests : IClassFixture<AuthWebApplicationFactory>
{
    private const string Password = "Password1!";

    private readonly AuthWebApplicationFactory _factory;

    public ErrorLogTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutInternals()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/test/throw/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ThrowingProbeController.Message, body);
        Assert.DoesNotContain(nameof(ThrowingProbeController), body);
        Assert.False(string.IsNullOrWhiteSpace(TraceIdFrom(body)));
    }

    [Fact]
    public async Task UnhandledException_IsStoredWithMatchingTraceId()
    {
        var tag = Guid.NewGuid().ToString("N");

        var response = await _factory.CreateClient().GetAsync($"/api/test/throw/{tag}");

        var entry = await SingleEntryAsync(_factory, tag);
        Assert.Equal(TraceIdFrom(await response.Content.ReadAsStringAsync()), entry.TraceId);
        Assert.Equal("GET", entry.Method);
        Assert.Equal($"/api/test/throw/{tag}", entry.Path);
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionType);
        Assert.Equal(ThrowingProbeController.Message, entry.Message);
        Assert.Contains(nameof(ThrowingProbeController), entry.StackTrace);
    }

    [Fact]
    public async Task UnhandledException_StoresPathButNeverQueryString()
    {
        var tag = Guid.NewGuid().ToString("N");
        const string secret = "reset-token-must-not-be-stored";

        await _factory.CreateClient().GetAsync($"/api/test/throw/{tag}?token={secret}");

        var entry = await SingleEntryAsync(_factory, tag);
        Assert.Equal($"/api/test/throw/{tag}", entry.Path);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(entry));
    }

    [Fact]
    public async Task UnhandledException_RecordsSignedInUserButNotAnonymous()
    {
        var (userId, email) = await SeedAsync(_factory);
        var client = await SignedInAsync(_factory, email);
        var signedInTag = Guid.NewGuid().ToString("N");
        var anonymousTag = Guid.NewGuid().ToString("N");

        await client.GetAsync($"/api/test/throw/{signedInTag}");
        await _factory.CreateClient().GetAsync($"/api/test/throw/{anonymousTag}");

        Assert.Equal(userId, (await SingleEntryAsync(_factory, signedInTag)).UserId);
        Assert.Null((await SingleEntryAsync(_factory, anonymousTag)).UserId);
    }

    [Fact]
    public async Task HandledClientErrors_WriteNoRow()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();

        var badLogin = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"nobody.{Guid.NewGuid():N}@example.com", Password));
        var unknownRoute = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ErrorLogEntries.CountAsync());
    }

    [Fact]
    public async Task FailedLogWrite_StillReturnsProblemDetails()
    {
        await using var factory = new FailingErrorLogWebApplicationFactory();

        var response = await factory.CreateClient().GetAsync($"/api/test/throw/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(TraceIdFrom(await response.Content.ReadAsStringAsync())));
        // Proves the write really failed, rather than the test passing on a working database.
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ErrorLogEntries.CountAsync());
    }

    [Fact]
    public async Task NewEntry_PurgesRowsOlderThanThirtyDays()
    {
        await using var factory = new AuthWebApplicationFactory();
        var old = Entry(DateTime.UtcNow.AddDays(-31));
        var recent = Entry(DateTime.UtcNow.AddDays(-29));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ErrorLogEntries.AddRange(old, recent);
            await db.SaveChangesAsync();
        }

        await factory.CreateClient().GetAsync($"/api/test/throw/{Guid.NewGuid():N}");

        using var check = factory.Services.CreateScope();
        var ids = await check.ServiceProvider.GetRequiredService<AppDbContext>()
            .ErrorLogEntries.Select(entry => entry.Id).ToListAsync();
        Assert.DoesNotContain(old.Id, ids);
        Assert.Contains(recent.Id, ids);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task ListErrors_RefusesAnonymousAndRegularUsers()
    {
        var (_, email) = await SeedAsync(_factory);
        var user = await SignedInAsync(_factory, email);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync("/api/admin/errors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/errors")).StatusCode);
    }

    [Fact]
    public async Task ListErrors_ForAdmin_ReturnsNewestFirstWithPaging()
    {
        await using var factory = new AuthWebApplicationFactory();
        var oldest = Entry(DateTime.UtcNow.AddHours(-3));
        var middle = Entry(DateTime.UtcNow.AddHours(-2));
        var newest = Entry(DateTime.UtcNow.AddHours(-1));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ErrorLogEntries.AddRange(middle, oldest, newest);
            await db.SaveChangesAsync();
        }

        var (_, adminEmail) = await SeedAsync(factory, UserRole.Admin);
        var admin = await SignedInAsync(factory, adminEmail);

        var first = await admin.GetFromJsonAsync<ErrorLogListResponse>("/api/admin/errors?page=1&pageSize=2");
        var second = await admin.GetFromJsonAsync<ErrorLogListResponse>("/api/admin/errors?page=2&pageSize=2");

        Assert.Equal(3, first!.Total);
        Assert.Equal([newest.Id, middle.Id], first.Items.Select(item => item.Id));
        Assert.Equal([oldest.Id], second!.Items.Select(item => item.Id));
    }

    private static ErrorLogEntry Entry(DateTime occurredAt) => new()
    {
        OccurredAt = occurredAt,
        TraceId = Guid.NewGuid().ToString("N"),
        Method = "GET",
        Path = "/seeded",
        StatusCode = 500,
        ExceptionType = "System.Exception",
        Message = "seeded",
        StackTrace = "seeded",
    };

    private static string? TraceIdFrom(string problemJson) =>
        JsonDocument.Parse(problemJson).RootElement.TryGetProperty("traceId", out var traceId)
            ? traceId.GetString()
            : null;

    private static async Task<ErrorLogEntry> SingleEntryAsync(AuthWebApplicationFactory factory, string tag)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ErrorLogEntries.AsNoTracking().SingleAsync(entry => entry.Path.EndsWith(tag));
    }

    private static async Task<(Guid Id, string Email)> SeedAsync(
        AuthWebApplicationFactory factory, UserRole role = UserRole.User)
    {
        var email = $"user.{Guid.NewGuid():N}@example.com";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = email,
            Name = "Test User",
            Role = role,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    /// <summary>Password login, then the emailed code.</summary>
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
}

/// <summary>Test-only route that always throws, standing in for any unexpected server failure.</summary>
[ApiController]
[Route("api/test/throw")]
[AllowAnonymous]
public sealed class ThrowingProbeController : ControllerBase
{
    public const string Message = "Probe failure for the error log.";

    [HttpGet("{tag}")]
    public IActionResult Get(string tag) => throw new InvalidOperationException(Message);
}

/// <summary>Factory whose database refuses to save error log rows, as when Postgres is down.</summary>
public sealed class FailingErrorLogWebApplicationFactory : AuthWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(new RefuseErrorLogSave())));
    }

    private sealed class RefuseErrorLogSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ErrorLogEntry>().Any())
            {
                throw new InvalidOperationException("Database is down.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
