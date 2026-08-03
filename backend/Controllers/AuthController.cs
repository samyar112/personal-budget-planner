using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher) : ControllerBase
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
}
