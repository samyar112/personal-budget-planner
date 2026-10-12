using backend.Services;
using backend.Services.Email;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class EmailExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                options => options.Provider is EmailOptions.FileProvider or EmailOptions.SmtpProvider,
                "Email:Provider must be File or Smtp.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FromAddress),
                "Email:FromAddress is required.")
            .Validate<IHostEnvironment>(
                (options, environment) =>
                    options.Provider != EmailOptions.FileProvider || environment.IsDevelopment(),
                "Email:Provider=File writes live tokens to disk and is only allowed in Development.")
            .Validate(
                options => options.Provider != EmailOptions.FileProvider
                    || !string.IsNullOrWhiteSpace(options.OutputDirectory),
                "Email:OutputDirectory is required for the File provider.")
            .Validate(
                options => options.Provider != EmailOptions.SmtpProvider
                    || (!string.IsNullOrWhiteSpace(options.Smtp.Host)
                        && options.Smtp.Port > 0
                        && !string.IsNullOrWhiteSpace(options.Smtp.Username)
                        && !string.IsNullOrWhiteSpace(options.Smtp.Password)),
                "Email:Smtp requires Host, Port, Username and Password.")
            .ValidateOnStart();

        services.AddScoped<IEmailSender>(provider =>
            provider.GetRequiredService<IOptions<EmailOptions>>().Value.Provider == EmailOptions.SmtpProvider
                ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider)
                : ActivatorUtilities.CreateInstance<FileEmailSender>(provider));

        return services;
    }
}
