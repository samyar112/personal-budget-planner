using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public enum AdminUserResult
{
    Done,
    NotFound,

    /// <summary>Deactivating yourself or another admin is not allowed.</summary>
    Refused,
}

/// <summary>
/// Activates and deactivates accounts. The flag, the session revocation and the audit row go in
/// one save, so none of them can happen without the others.
/// </summary>
public sealed class AdminUserService(AppDbContext db)
{
    private const int MaxConflictRetries = 3;

    public async Task<AdminUserResult> SetActiveAsync(
        Guid adminId,
        Guid targetId,
        bool active,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TrySetActiveAsync(adminId, targetId, active, cancellationToken);
            }
            // A session rotated while we revoked it: reload and revoke the new token too.
            catch (DbUpdateConcurrencyException) when (attempt < MaxConflictRetries - 1)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<AdminUserResult> TrySetActiveAsync(
        Guid adminId,
        Guid targetId,
        bool active,
        CancellationToken cancellationToken)
    {
        var target = await db.Users.FirstOrDefaultAsync(user => user.Id == targetId, cancellationToken);
        if (target is null)
        {
            return AdminUserResult.NotFound;
        }

        if (!active && (target.Id == adminId || target.Role == UserRole.Admin))
        {
            return AdminUserResult.Refused;
        }

        if (target.IsActive == active)
        {
            return AdminUserResult.Done;
        }

        target.IsActive = active;

        if (!active)
        {
            var now = DateTime.UtcNow;
            var live = await db.RefreshTokens
                .Where(token => token.UserId == target.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in live)
            {
                token.RevokedAt = now;
                token.GraceExpiresAt = null;
            }
        }

        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            AdminId = adminId,
            TargetUserId = target.Id,
            Action = active ? AdminAction.Activate : AdminAction.Deactivate,
        });

        await db.SaveChangesAsync(cancellationToken);
        return AdminUserResult.Done;
    }
}
