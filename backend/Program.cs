using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend.Data;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ProxyOptions>()
    .Bind(builder.Configuration.GetSection(ProxyOptions.SectionName))
    .Validate(
        options => options.KnownNetworks.All(network => System.Net.IPNetwork.TryParse(network, out _)),
        "Proxy:KnownNetworks must be CIDR ranges such as 10.0.0.0/8.")
    .ValidateOnStart();

// Reverse proxies (Tailscale Funnel, Vercel rewrite) terminate TLS, so honour the forwarded headers,
// but only from the configured proxy ranges. Trusting every peer would let a client spoof its IP.
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<ProxyOptions>>((options, proxy) =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in proxy.Value.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });

builder.Services.AddDbContext<backend.Data.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthSecurity(builder.Configuration);
builder.Services.AddEmail(builder.Configuration);
builder.Services.AddHostValidation();
builder.Services.AddAdminAccess(builder.Configuration);

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Key) && options.Key.Length >= 32,
        "Jwt:Key must be at least 32 characters. Set it in appsettings.Development.json.")
    .ValidateOnStart();

builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<LoginCodeService>();

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

            // A signed token outlives deactivation, so confirm the account on every request.
            // Fails closed: a missing user or bad subject is a 401, and a lookup error never lets the request through.
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (!Guid.TryParse(subject, out var userId))
                {
                    context.Fail("Invalid subject.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var active = await db.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => (bool?)user.IsActive)
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                if (active != true)
                {
                    context.Fail("Account is inactive.");
                }
            },
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthSecurityExtensions.VerifiedEmailPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(TokenProvider.EmailVerifiedClaim, "true")));

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
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
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
