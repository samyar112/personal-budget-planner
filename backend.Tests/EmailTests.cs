using backend.Extensions;
using backend.Services;
using backend.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace backend.Tests;

/*
 * Test cases:
 * 1. File sender writes an .eml with recipient, subject and body.
 * 2. File sender creates a missing output directory.
 * 3. Verification template carries the link in text and HTML, HTML-encoded.
 * 4. Password reset template carries the link and a different subject.
 * 5. File provider is rejected outside Development.
 * 6. Smtp provider without host or credentials is rejected.
 * 7. Smtp provider with full config is accepted in Production.
 * 8. Unknown provider is rejected.
 * 9. A plain-http frontend URL is rejected outside Development.
 * 10. A non-positive verification lifetime is rejected.
 *
 * Not covered: SmtpEmailSender talks to a real server. Checked by hand against Gmail.
 */
public sealed class EmailTests : IDisposable
{
    private const string Link = "https://app.example.com/verify?token=abc&uid=7";

    private readonly string _outbox = Path.Combine(Path.GetTempPath(), $"outbox-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outbox))
        {
            Directory.Delete(_outbox, recursive: true);
        }
    }

    [Fact]
    public async Task File_sender_writes_eml_with_recipient_subject_and_body()
    {
        var sender = new FileEmailSender(Options.Create(FileOptions()));
        var message = EmailTemplates.Verification("jane@example.com", Link, TimeSpan.FromHours(24));

        await sender.SendAsync(message, CancellationToken.None);

        var file = Assert.Single(Directory.GetFiles(_outbox, "*.eml"));
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("jane@example.com", content);
        Assert.Contains(message.Subject, content);
        Assert.Contains("token=abc", content);
    }

    [Fact]
    public async Task File_sender_creates_missing_output_directory()
    {
        var nested = Path.Combine(_outbox, "a", "b");
        var sender = new FileEmailSender(Options.Create(FileOptions(nested)));

        await sender.SendAsync(
            EmailTemplates.PasswordReset("jane@example.com", Link, TimeSpan.FromHours(1)),
            CancellationToken.None);

        Assert.Single(Directory.GetFiles(nested, "*.eml"));
    }

    [Fact]
    public void Verification_template_carries_link_in_text_and_encoded_in_html()
    {
        var message = EmailTemplates.Verification("jane@example.com", Link, TimeSpan.FromHours(24));

        Assert.Equal("jane@example.com", message.To);
        Assert.Contains(Link, message.TextBody);
        Assert.Contains("token=abc&amp;uid=7", message.HtmlBody);
        Assert.DoesNotContain("abc&uid", message.HtmlBody);
    }

    [Fact]
    public void Password_reset_template_carries_link_and_has_its_own_subject()
    {
        var verification = EmailTemplates.Verification("jane@example.com", Link, TimeSpan.FromHours(24));
        var reset = EmailTemplates.PasswordReset("jane@example.com", Link, TimeSpan.FromHours(1));

        Assert.Contains(Link, reset.TextBody);
        Assert.NotEqual(verification.Subject, reset.Subject);
    }

    [Fact]
    public void File_provider_is_rejected_outside_development()
    {
        var options = Resolve("Production", new()
        {
            ["Email:Provider"] = "File",
            ["Email:FromAddress"] = "me@gmail.com",
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Smtp_provider_without_host_or_credentials_is_rejected()
    {
        var options = Resolve("Development", new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:FromAddress"] = "me@gmail.com",
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Smtp_provider_with_full_config_is_accepted_in_production()
    {
        var options = Resolve("Production", new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:FromAddress"] = "me@gmail.com",
            ["Email:Smtp:Host"] = "smtp.gmail.com",
            ["Email:Smtp:Port"] = "587",
            ["Email:Smtp:Username"] = "me@gmail.com",
            ["Email:Smtp:Password"] = "not-a-real-password",
            ["Email:FrontendBaseUrl"] = "https://app.example.com",
        });

        Assert.Equal("smtp.gmail.com", options.Value.Smtp.Host);
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        var options = Resolve("Development", new()
        {
            ["Email:Provider"] = "Carrier-pigeon",
            ["Email:FromAddress"] = "me@gmail.com",
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Http_frontend_url_is_rejected_outside_development()
    {
        var options = Resolve("Production", new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:FromAddress"] = "me@gmail.com",
            ["Email:FrontendBaseUrl"] = "http://app.example.com",
            ["Email:Smtp:Host"] = "smtp.gmail.com",
            ["Email:Smtp:Username"] = "me@gmail.com",
            ["Email:Smtp:Password"] = "not-a-real-password",
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Non_positive_verification_lifetime_is_rejected()
    {
        var options = Resolve("Development", new()
        {
            ["Email:Provider"] = "File",
            ["Email:FromAddress"] = "me@gmail.com",
            ["Email:VerificationTokenMinutes"] = "0",
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    private EmailOptions FileOptions(string? directory = null) => new()
    {
        Provider = EmailOptions.FileProvider,
        FromAddress = "no-reply@vaultbudget.test",
        OutputDirectory = directory ?? _outbox,
    };

    private static IOptions<EmailOptions> Resolve(string environment, Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        services.AddEmail(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<EmailOptions>>();
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "backend.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
