namespace backend.Services;

/// <summary>
/// First-admin bootstrap. There is deliberately no endpoint that changes a role, so the only
/// way in is naming an existing, verified account here (Admin__Email in the environment).
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Optional. Empty means nobody is promoted.</summary>
    public string? Email { get; set; }
}
