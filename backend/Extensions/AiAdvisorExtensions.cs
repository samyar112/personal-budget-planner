using backend.Services;
using Microsoft.Extensions.Options;

namespace backend.Extensions;

public static class AiAdvisorExtensions
{
    /// <summary>
    /// The key is optional: without one, tips are template text and nothing leaves the API. A key with no model
    /// is a misconfiguration and fails at boot.
    /// </summary>
    public static IServiceCollection AddAiAdvisor(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AiAdvisorOptions>()
            .Bind(configuration.GetSection(AiAdvisorOptions.SectionName))
            .Validate(options => AiAdvisorOptions.IsHttpsUrl(options.BaseUrl), "AiAdvisor:BaseUrl must be an https URL.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.ApiKey) || !string.IsNullOrWhiteSpace(options.Model),
                "AiAdvisor:Model is required when AiAdvisor:ApiKey is set.")
            .Validate(options => options.TimeoutSeconds is >= 1 and <= 120, "AiAdvisor:TimeoutSeconds must be 1-120.")
            .Validate(options => options.DailyLimitPerUser is >= 1 and <= 1000, "AiAdvisor:DailyLimitPerUser must be 1-1000.")
            .ValidateOnStart();

        services.AddScoped<AiTipsService>();
        services.AddHttpClient<IAdvisorModel, OpenRouterAdvisorModel>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<AiAdvisorOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
