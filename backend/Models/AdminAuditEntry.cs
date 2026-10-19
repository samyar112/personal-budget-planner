namespace backend.Models;

public enum AdminAction
{
    Deactivate,
    Activate,
}

/// <summary>
/// One admin action on one account. Written in the same save as the change it records,
/// so an action cannot happen without leaving a row.
/// </summary>
public class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdminId { get; set; }

    public Guid TargetUserId { get; set; }

    public AdminAction Action { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
