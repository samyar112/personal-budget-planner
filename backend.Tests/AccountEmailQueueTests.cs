using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using backend.DTOs;
using backend.Services.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace backend.Tests;

/*
 * Test cases:
 * 1. forgot-password answers a known and an unknown address equally fast, even when mail is slow.
 * 2. resend-verification answers a known and an unknown address equally fast, even when mail is slow.
 * 3. The queued reset email is still delivered to the known address afterwards.
 * 4. The queued verification email is still delivered to the known address afterwards.
 * 5. A failed send does not stop the worker: the next job is still delivered.
 */
public sealed class AccountEmailQueueTests
{
    private const string Password = "Password1!";
    private static readonly TimeSpan MailDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FastEnough = TimeSpan.FromMilliseconds(800);

    [Fact]
    public async Task ForgotPassword_RespondsQuicklyForKnownAndUnknown_WhenMailIsSlow()
    {
        await using var factory = new SlowMailWebApplicationFactory(MailDelay);
        var client = factory.CreateClient();
        var email = await RegisterAsync(factory, client);

        var known = await TimeAsync(() => client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email)));
        var unknown = await TimeAsync(() => client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest($"nobody.{Guid.NewGuid():N}@example.com")));

        Assert.Equal(HttpStatusCode.Accepted, known.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.Response.StatusCode);
        Assert.True(known.Elapsed < FastEnough, $"Known address took {known.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.True(unknown.Elapsed < FastEnough, $"Unknown address took {unknown.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task ResendVerification_RespondsQuicklyForKnownAndUnknown_WhenMailIsSlow()
    {
        await using var factory = new SlowMailWebApplicationFactory(MailDelay);
        var client = factory.CreateClient();
        var email = await RegisterAsync(factory, client);

        var known = await TimeAsync(() => client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest(email)));
        var unknown = await TimeAsync(() => client.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new ResendVerificationRequest($"nobody.{Guid.NewGuid():N}@example.com")));

        Assert.Equal(HttpStatusCode.Accepted, known.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.Response.StatusCode);
        Assert.True(known.Elapsed < FastEnough, $"Known address took {known.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.True(unknown.Elapsed < FastEnough, $"Unknown address took {unknown.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task ForgotPassword_StillDeliversTheResetEmail_Afterwards()
    {
        await using var factory = new SlowMailWebApplicationFactory(MailDelay);
        var client = factory.CreateClient();
        var email = await RegisterAsync(factory, client);

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        var message = await WaitForAsync(factory, email, "/reset-password?token=");
        Assert.NotNull(message);
    }

    [Fact]
    public async Task ResendVerification_StillDeliversTheVerificationEmail_Afterwards()
    {
        await using var factory = new SlowMailWebApplicationFactory(MailDelay);
        var client = factory.CreateClient();
        var email = await RegisterAsync(factory, client);
        var before = factory.Emails.Sent.Count(message => message.To == email);

        await client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest(email));

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && factory.Emails.Sent.Count(message => message.To == email) <= before)
        {
            await Task.Delay(50);
        }

        Assert.True(factory.Emails.Sent.Count(message => message.To == email) > before);
    }

    [Fact]
    public async Task WorkerKeepsRunning_AfterASendFails()
    {
        await using var factory = new FailFirstMailWebApplicationFactory();
        var client = factory.CreateClient();
        var first = await RegisterAsync(factory, client);
        var second = await RegisterAsync(factory, client);

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(first));
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(second));

        var delivered = await WaitForAsync(factory, second, "/reset-password?token=");
        Assert.NotNull(delivered);
    }

    private static async Task<(HttpResponseMessage Response, TimeSpan Elapsed)> TimeAsync(
        Func<Task<HttpResponseMessage>> send)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await send();
        stopwatch.Stop();
        return (response, stopwatch.Elapsed);
    }

    /// <summary>Registers a user, then waits for the signup email so later counts start clean.</summary>
    private static async Task<string> RegisterAsync(AuthWebApplicationFactory factory, HttpClient client)
    {
        var email = $"queue.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Jane", "Doe", email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private static async Task<EmailMessage?> WaitForAsync(AuthWebApplicationFactory factory, string address, string contains)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var match = factory.Emails.Sent.FirstOrDefault(
                message => message.To == address && message.TextBody.Contains(contains));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        return null;
    }
}

/// <summary>Real queue and worker, with a mail sender that takes a while to finish.</summary>
public sealed class SlowMailWebApplicationFactory(TimeSpan delay) : AuthWebApplicationFactory
{
    protected override bool UseInlineEmailQueue => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(provider =>
                new DelayedEmailSender(provider.GetRequiredService<CapturingEmailSender>(), delay));
        });
    }
}

/// <summary>Real queue and worker, where the first send throws and later ones succeed.</summary>
public sealed class FailFirstMailWebApplicationFactory : AuthWebApplicationFactory
{
    protected override bool UseInlineEmailQueue => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(provider =>
                new FailFirstEmailSender(provider.GetRequiredService<CapturingEmailSender>()));
        });
    }
}

public sealed class DelayedEmailSender(CapturingEmailSender inner, TimeSpan delay) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        await inner.SendAsync(message, cancellationToken);
    }
}

public sealed class FailFirstEmailSender(CapturingEmailSender inner) : IEmailSender
{
    private int _calls;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        Interlocked.Increment(ref _calls) == 1
            ? throw new InvalidOperationException("SMTP is down.")
            : inner.SendAsync(message, cancellationToken);
}
