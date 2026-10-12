using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TokenProvider tokenProvider,
    RefreshTokenService refreshTokenService,
    IOptions<AuthSecurityOptions> authSecurityOptions,
    EmailVerificationService emailVerification,
    ILogger<AuthController> logger,
    IHostEnvironment environment) : ControllerBase
{
    /// <summary>
    /// Registers a new user with email/password. Passwords are hashed before storage.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var fieldErrors = AuthValidation.ValidateRegister(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password);

        if (fieldErrors.Count > 0)
        {
            foreach (var (key, message) in fieldErrors)
            {
                ModelState.AddModelError(key, message);
            }

            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var emailTaken = await db.Users.AnyAsync(
            user => user.Email == email,
            cancellationToken);

        if (emailTaken)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var fullName = $"{request.FirstName.Trim()} {request.LastName.Trim()}";
        var user = new User
        {
            Email = email,
            Name = fullName,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        // A mail failure must not undo the signup; the user can request a resend.
        try
        {
            await emailVerification.SendAsync(user, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Verification email failed for user {UserId}.", user.Id);
        }

        return Created(
            $"/api/auth/register/{user.Id}",
            new RegisterResponse(user.Id, user.Email, user.Name));
    }

    /// <summary>
    /// Authenticates with email/password and sets HttpOnly access + refresh cookies.
    /// Rate-limited per IP; locks the account after repeated failed attempts.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.LoginRateLimitPolicy)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var fieldErrors = AuthValidation.ValidateLogin(request.Email, request.Password);
        if (fieldErrors.Count > 0)
        {
            foreach (var (key, message) in fieldErrors)
            {
                ModelState.AddModelError(key, message);
            }

            return ValidationProblem(ModelState);
        }

        var security = authSecurityOptions.Value;
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);

        // Same message whether missing user or bad password — avoid account enumeration.
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (AuthSecurityExtensions.IsLockedOut(user, DateTime.UtcNow))
        {
            return StatusCode(
                StatusCodes.Status423Locked,
                new { message = "Your account is temporarily locked. Please try again later." });
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            AuthSecurityExtensions.RegisterFailedLogin(user, security);
            await db.SaveChangesAsync(cancellationToken);

            if (AuthSecurityExtensions.IsLockedOut(user, DateTime.UtcNow))
            {
                return StatusCode(
                    StatusCodes.Status423Locked,
                    new { message = "Your account is temporarily locked. Please try again later." });
            }

            return Unauthorized(new { message = "Invalid email or password." });
        }

        AuthSecurityExtensions.ClearLoginFailures(user);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokenProvider.Create(user);
        var refresh = await refreshTokenService.IssueAsync(user, cancellationToken);
        WriteAuthCookies(access, refresh);

        return Ok(new LoginResponse(access.ExpiresAt, user.Email, user.Name));
    }

    /// <summary>
    /// Exchanges a valid refresh cookie for a new access JWT (and rotates the refresh token).
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(AuthCookie.RefreshTokenName, out var rawRefresh)
            || string.IsNullOrWhiteSpace(rawRefresh))
        {
            return Unauthorized(new { message = "Refresh token is missing or invalid." });
        }

        var outcome = await refreshTokenService.RefreshAsync(rawRefresh, cancellationToken);
        if (outcome is null)
        {
            AuthCookie.ClearAll(Response, environment.IsDevelopment());
            return Unauthorized(new { message = "Refresh token is missing or invalid." });
        }

        AuthCookie.SetAccessToken(
            Response,
            outcome.AccessToken.AccessToken,
            outcome.AccessToken.ExpiresAt,
            environment.IsDevelopment());

        // Grace-period reuse: access only; refresh cookie already updated by the winning request.
        if (outcome.NewRefreshToken is { } newRefresh)
        {
            AuthCookie.SetRefreshToken(
                Response,
                newRefresh.RawToken,
                newRefresh.ExpiresAt,
                environment.IsDevelopment());
        }

        return Ok(new RefreshResponse(outcome.AccessToken.ExpiresAt));
    }

    /// <summary>
    /// Revokes the refresh token (when present) and clears auth cookies.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(AuthCookie.RefreshTokenName, out var rawRefresh))
        {
            await refreshTokenService.RevokeAsync(rawRefresh, cancellationToken);
        }

        AuthCookie.ClearAll(Response, environment.IsDevelopment());
        return NoContent();
    }

    /// <summary>
    /// Returns the authenticated user from the JWT cookie (or Bearer header).
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new MeResponse(user.Id, user.Email, user.Name, user.EmailVerifiedAt is not null));
    }

    /// <summary>
    /// Consumes the emailed token and marks the address verified. Tokens are single-use and expire.
    /// </summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var result = await emailVerification.VerifyAsync(request.Token, cancellationToken);
        return result switch
        {
            VerifyEmailResult.Verified => Ok(new { message = "Email verified." }),
            VerifyEmailResult.Expired => BadRequest(new
            {
                code = "expired",
                message = "This verification link has expired. Request a new one.",
            }),
            _ => BadRequest(new
            {
                code = "invalid",
                message = "This verification link is invalid or has already been used.",
            }),
        };
    }

    /// <summary>
    /// Sends a fresh verification email. Always answers 202 so it cannot be used to
    /// discover which addresses have accounts; rate-limited per IP.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.ResendVerificationRateLimitPolicy)]
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);

        if (user is not null && user.EmailVerifiedAt is null)
        {
            try
            {
                await emailVerification.SendAsync(user, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Verification resend failed for user {UserId}.", user.Id);
            }
        }

        return Accepted(new { message = "If that account needs verification, an email is on its way." });
    }

    private void WriteAuthCookies(TokenResult access, IssuedRefreshToken refresh)
    {
        var isDevelopment = environment.IsDevelopment();
        AuthCookie.SetAccessToken(Response, access.AccessToken, access.ExpiresAt, isDevelopment);
        AuthCookie.SetRefreshToken(Response, refresh.RawToken, refresh.ExpiresAt, isDevelopment);
    }
}
