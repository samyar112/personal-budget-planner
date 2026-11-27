namespace backend.Models;

public enum LoginMethod
{
    /// <summary>Password alone: an account whose email is not yet verified skips the code step.</summary>
    Password,

    /// <summary>Password, then the emailed one-time code.</summary>
    EmailCode,

    Google,
}

/// <summary>
/// One login, from sign-in until logout or revocation. Its id is the refresh-token <c>FamilyId</c>, so every
/// rotation of that login belongs to it. Activity is seen only at refresh, so <c>LastSeenAt</c> is accurate to
/// roughly one access-token lifetime. No IP address or user agent is kept.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public LoginMethod LoginMethod { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    /// <summary>Set by logout, replay revocation, deactivation, password reset or a Google claim.</summary>
    public DateTime? EndedAt { get; set; }
}
