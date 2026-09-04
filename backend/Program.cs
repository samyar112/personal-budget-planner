using System.Text;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // App Service / reverse proxies terminate TLS; trust forwarded headers from the edge.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDbContext<backend.Data.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthSecurity(builder.Configuration);

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Key) && options.Key.Length >= 32,
        "Jwt:Key must be at least 32 characters. Set it in appsettings.Development.json.")
    .ValidateOnStart();

builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddScoped<RefreshTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Bind bearer validation to the same JwtOptions used by TokenProvider (critical for tests).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtAccessor) =>
    {
        var jwt = jwtAccessor.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        // Prefer Authorization: Bearer (handled after this event); else use HttpOnly cookie.
        bearerOptions.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!string.IsNullOrEmpty(context.Request.Headers.Authorization))
                {
                    return Task.CompletedTask;
                }

                if (context.Request.Cookies.TryGetValue(AuthCookie.AccessTokenName, out var cookieToken)
                    && !string.IsNullOrWhiteSpace(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

const string frontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(frontendCorsPolicy);
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposes Program to WebApplicationFactory for integration tests.
public partial class Program;
