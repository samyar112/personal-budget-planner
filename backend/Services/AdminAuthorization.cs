using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

/// <summary>Satisfied only when the database, not the token, says the caller is an active admin.</summary>
public sealed class CurrentAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// The role claim in the JWT can be up to an access-token lifetime stale. For admin routes that
/// is too long, so each request re-reads the account. Admin traffic is tiny, so one query is cheap.
/// </summary>
public sealed class CurrentAdminHandler(AppDbContext db) : AuthorizationHandler<CurrentAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentAdminRequirement requirement)
    {
        var subject =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(subject, out var userId))
        {
            return;
        }

        var isAdmin = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Admin && user.IsActive);

        if (isAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
