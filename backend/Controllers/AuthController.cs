using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TokenProvider tokenProvider,
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

        return Created(
            $"/api/auth/register/{user.Id}",
            new RegisterResponse(user.Id, user.Email, user.Name));
    }
 
    /// <summary>
    /// Authenticates with email/password and sets an HttpOnly JWT cookie.
    /// </summary>
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

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);

        // Same message whether missing user or bad password — avoid account enumeration.
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var token = tokenProvider.Create(user);
        AuthCookie.SetAccessToken(
            Response,
            token.AccessToken,
            token.ExpiresAt,
            environment.IsDevelopment());

        return Ok(new LoginResponse(token.ExpiresAt, user.Email, user.Name));
    }

    /// <summary>
    /// Clears the access-token cookie.
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        AuthCookie.ClearAccessToken(Response, environment.IsDevelopment());
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

        return Ok(new MeResponse(user.Id, user.Email, user.Name));
    }
}
