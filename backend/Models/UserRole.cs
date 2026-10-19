namespace backend.Models;

/// <summary>
/// Two fixed roles, stored as their names. An admin manages accounts and never sees ledger data.
/// </summary>
public enum UserRole
{
    User,
    Admin,
}
