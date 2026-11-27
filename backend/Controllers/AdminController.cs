using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Controllers;

/// <summary>
/// Account management for admins. Nothing here reads the ledger: an admin sees who has an
/// account and whether it works, never what is in it.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AdminExtensions.AdminPolicy)]
public sealed class AdminController(
    AppDbContext db,
    AdminUserService adminUsers,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Lists accounts, oldest first. <c>verified=false</c> is the unverified-email list.
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType<AdminUserListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserListResponse>> ListUsers(
        [FromQuery] bool? verified,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var now = DateTime.UtcNow;

        var query = db.Users.AsNoTracking();
        if (verified is { } wantVerified)
        {
            query = query.Where(user => (user.EmailVerifiedAt != null) == wantVerified);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(user => user.DateEntered)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.Name,
                user.Role,
                EmailVerified = user.EmailVerifiedAt != null,
                user.IsActive,
                IsLocked = user.LockoutEnd != null && user.LockoutEnd > now,
                CreatedAt = user.DateEntered,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AdminUserDto(
                row.Id, row.Email, row.Name, row.Role.ToString(),
                row.EmailVerified, row.IsActive, row.IsLocked, row.CreatedAt))
            .ToList();

        return Ok(new AdminUserListResponse(items, page, pageSize, total));
    }

    /// <summary>
    /// Logins, most recently seen first. <c>active=true</c> keeps those not ended and seen within one
    /// access-token lifetime: activity is only observed at refresh, so that is the finest honest window.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType<UserSessionListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserSessionListResponse>> ListSessions(
        [FromQuery] bool? active,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var activeSince = DateTime.UtcNow.AddMinutes(-jwtOptions.Value.ExpiresMinutes);

        var query = db.UserSessions.AsNoTracking();
        if (active is { } wantActive)
        {
            query = wantActive
                ? query.Where(session => session.EndedAt == null && session.LastSeenAt >= activeSince)
                : query.Where(session => session.EndedAt != null || session.LastSeenAt < activeSince);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(session => session.LastSeenAt)
            .ThenBy(session => session.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(session => new
            {
                session.Id,
                session.UserId,
                session.User.Email,
                session.User.Name,
                session.LoginMethod,
                session.StartedAt,
                session.LastSeenAt,
                session.EndedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new UserSessionDto(
                row.Id, row.UserId, row.Email, row.Name, row.LoginMethod.ToString(),
                row.StartedAt, row.LastSeenAt, row.EndedAt,
                IsActive: row.EndedAt == null && row.LastSeenAt >= activeSince))
            .ToList();

        return Ok(new UserSessionListResponse(items, page, pageSize, total));
    }

    /// <summary>
    /// Unhandled server errors, newest first. Rows older than 30 days are purged as new ones arrive.
    /// </summary>
    [HttpGet("errors")]
    [ProducesResponseType<ErrorLogListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ErrorLogListResponse>> ListErrors(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var total = await db.ErrorLogEntries.CountAsync(cancellationToken);
        var items = await db.ErrorLogEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new ErrorLogEntryDto(
                entry.Id, entry.OccurredAt, entry.TraceId, entry.Method, entry.Path, entry.StatusCode,
                entry.ExceptionType, entry.Message, entry.StackTrace, entry.UserId))
            .ToListAsync(cancellationToken);

        return Ok(new ErrorLogListResponse(items, page, pageSize, total));
    }

    /// <summary>
    /// Blocks login and refresh and ends every session. An access token already issued works until
    /// it expires. Repeating the call changes nothing. Admins, including yourself, cannot be deactivated.
    /// </summary>
    [HttpPost("users/{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: false, cancellationToken);

    [HttpPost("users/{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: true, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var adminId))
        {
            return Unauthorized();
        }

        return await adminUsers.SetActiveAsync(adminId, id, active, cancellationToken) switch
        {
            AdminUserResult.Done => NoContent(),
            AdminUserResult.NotFound => NotFound(),
            _ => Conflict(new { message = "Admin accounts cannot be deactivated." }),
        };
    }
}
