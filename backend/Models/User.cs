namespace backend.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public DateTime DateEntered { get; set; } = DateTime.UtcNow;

    /// <summary>Consecutive failed password attempts since the last successful login.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>When set and in the future, login is rejected until this UTC time.</summary>
    public DateTime? LockoutEnd { get; set; }
}
