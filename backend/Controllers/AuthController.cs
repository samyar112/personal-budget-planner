using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
using backend.Services;
using backend.Services.Email;
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
    PasswordResetService passwordReset,
    LoginCodeService loginCode,
    IAccountEmailQueue accountEmails,
    IHostEnvironment environment) : ControllerBase
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string DeactivatedMessage = "This account has been deactivated. Contact an administrator.";

    private static readonly User DummyUser = new() { Email = string.Empty, Name = string.Empty };
    private static string? _dummyHash;

    private static string DummyHash(IPasswordHasher<User> hasher) =>
        LazyInitializer.EnsureInitialized(ref _dummyHash, () => hasher.HashPassword(DummyUser, "Dummy-Password-1"));

    /// <summary>
    /// Registers a new user with email/password. Passwords are hashed before storage.
    /// Always answers 201, whether or not the address already has an account, so it cannot be used
    /// to discover registered emails. Rate-limited per IP because every signup sends an email to an
    /// address of the caller's choosing.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.RegisterRateLimitPolicy)]
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

        // A new and an existing address get the same 201, do the same hashing and both hand their
        // email to the background queue, so neither the response nor its timing says which one it was.
        // The owner of an existing address is told by email instead.
        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = $"{request.FirstName.Trim()} {request.LastName.Trim()}";
        var user = new User
        {
            Email = email,
            Name = fullName,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var created = !await db.Users.AnyAsync(candidate => candidate.Email == email, cancellationToken);
        if (created)
        {
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost a race with another signup for the same address: it is an existing account now.
                db.Entry(user).State = EntityState.Detached;
                created = false;
            }
        }

        // A mail failure must not undo the signup; the user can request a resend.
        await accountEmails.EnqueueAsync(
            new AccountEmailJob(
                created ? AccountEmailKind.VerificationResend : AccountEmailKind.RegistrationNotice,
                email),
            cancellationToken);

        return Created(
            $"/api/auth/register/{user.Id}",
            new RegisterResponse(user.Id, email, fullName));
    }

    /// <summary>
    /// Authenticates with email/password. A verified email gets a 202 challenge and an emailed code
    /// (no cookies until the code passes); an unverified one gets the HttpOnly cookies directly.
    /// Rate-limited per IP; locks the account after repeated failed attempts. A locked account
    /// answers exactly like a wrong password, so the response never confirms an account exists.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.LoginRateLimitPolicy)]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LoginChallengeResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Login(
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

        // Every path below does one password verification and answers 401 with the same message,
        // so neither the body, the status nor the response time says whether the account exists
        // or is locked. A dummy hash stands in when there is nothing real to check.
        var hasPassword = user is not null && !string.IsNullOrEmpty(user.PasswordHash);
        var verification = passwordHasher.VerifyHashedPassword(
            user ?? DummyUser,
            hasPassword ? user!.PasswordHash! : DummyHash(passwordHasher),
            request.Password);

        if (user is null || !hasPassword || AuthSecurityExtensions.IsLockedOut(user, DateTime.UtcNow))
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            AuthSecurityExtensions.RegisterFailedLogin(user, security);
            await db.SaveChangesAsync(cancellationToken);

            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        // Said only after the password checks out, so it tells nothing to someone who lacks it.
        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = DeactivatedMessage });
        }

        // A verified user still owes the emailed code, and wrong codes count toward the same
        // lockout, so only the code step may clear the failures. Clearing here would hand someone
        // who knows the password a fresh batch of guesses on every login.
        // The code goes to the verified mailbox, so it only proves anything once that is established.
        if (user.EmailVerifiedAt is not null)
        {
            var challenge = await loginCode.IssueAsync(user, cancellationToken);
            return Accepted(new LoginChallengeResponse(challenge.ChallengeId, challenge.ResendAvailableAt));
        }

        AuthSecurityExtensions.ClearLoginFailures(user);
        await db.SaveChangesAsync(cancellationToken);
        return await SignInAsync(user, cancellationToken);
    }

    /// <summary>
    /// Consumes the emailed login code and, only then, sets the HttpOnly cookies. Wrong codes
    /// count toward account lockout and a challenge dies after a few of them.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.VerifyLoginCodeRateLimitPolicy)]
    [HttpPost("login/verify-code")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> VerifyLoginCode(
        [FromBody] VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginCode.VerifyAsync(request.ChallengeId, request.Code, cancellationToken);
        return result.Status switch
        {
            LoginCodeStatus.Success => await SignInAsync(result.User!, cancellationToken),
            LoginCodeStatus.Locked => StatusCode(
                StatusCodes.Status423Locked,
                new { message = "Your account is temporarily locked. Please try again later." }),
            _ => Unauthorized(new { message = "That code is incorrect or has expired. Log in again." }),
        };
    }

    /// <summary>
    /// Emails a fresh code for an open challenge. Short cooldown per challenge plus a per-IP limit.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.ResendLoginCodeRateLimitPolicy)]
    [HttpPost("login/resend-code")]
    [ProducesResponseType<LoginChallengeResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> ResendLoginCode(
        [FromBody] ResendLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginCode.ResendAsync(request.ChallengeId, cancellationToken);
        return result.Status switch
        {
            LoginCodeStatus.Success => Accepted(
                new LoginChallengeResponse(request.ChallengeId, result.ResendAvailableAt!.Value)),
            LoginCodeStatus.Cooldown => StatusCode(
                StatusCodes.Status429TooManyRequests,
                new { message = "Please wait before requesting another code." }),
            _ => Unauthorized(new { message = "This login has expired. Log in again." }),
        };
    }

    private async Task<ActionResult> SignInAsync(User user, CancellationToken cancellationToken)
    {
        // Covers an account deactivated between the password step and the code step.
        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = DeactivatedMessage });
        }

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

        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        return Ok(new MeResponse(user.Id, user.Email, user.Name, user.EmailVerifiedAt is not null, user.Role.ToString()));
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

        // Lookup and send happen in the background worker so response time is the same
        // whether or not the address has an account.
        await accountEmails.EnqueueAsync(
            new AccountEmailJob(AccountEmailKind.VerificationResend, request.Email.Trim().ToLowerInvariant()),
            cancellationToken);

        return Accepted(new { message = "If that account needs verification, an email is on its way." });
    }

    /// <summary>
    /// Emails a password-reset link. Always answers 202 so it cannot be used to discover
    /// which addresses have accounts; rate-limited per IP.
    /// </summary>
    [EnableRateLimiting(AuthSecurityExtensions.ForgotPasswordRateLimitPolicy)]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
            return ValidationProblem(ModelState);
        }

        await accountEmails.EnqueueAsync(
            new AccountEmailJob(AccountEmailKind.PasswordReset, request.Email.Trim().ToLowerInvariant()),
            cancellationToken);

        return Accepted(new { message = "If an account exists for that address, a reset email is on its way." });
    }

    /// <summary>
    /// Consumes the emailed token and sets a new password. Tokens are single-use and expire;
    /// success clears lockout and signs the account out everywhere.
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var newPassword = request.NewPassword ?? string.Empty;
        if (!AuthValidation.IsPasswordValid(newPassword))
        {
            ModelState.AddModelError(
                nameof(request.NewPassword),
                "Password must be at least 8 characters with upper, lower, number and special character.");
            return ValidationProblem(ModelState);
        }

        var result = await passwordReset.ResetAsync(request.Token, newPassword, cancellationToken);
        return result switch
        {
            ResetPasswordResult.Reset => Ok(new { message = "Password updated. Log in with your new password." }),
            ResetPasswordResult.Expired => BadRequest(new
            {
                code = "expired",
                message = "This reset link has expired. Request a new one.",
            }),
            _ => BadRequest(new
            {
                code = "invalid",
                message = "This reset link is invalid or has already been used.",
            }),
        };
    }

    private void WriteAuthCookies(TokenResult access, IssuedRefreshToken refresh)
    {
        var isDevelopment = environment.IsDevelopment();
        AuthCookie.SetAccessToken(Response, access.AccessToken, access.ExpiresAt, isDevelopment);
        AuthCookie.SetRefreshToken(Response, refresh.RawToken, refresh.ExpiresAt, isDevelopment);
    }
}
