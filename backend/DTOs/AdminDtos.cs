namespace backend.DTOs;

/// <summary>
/// One account as an admin sees it. Deliberately has no password hash, token, Google subject or
/// anything from the ledger: admins manage accounts, not data.
/// </summary>
public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string Name,
    string Role,
    bool EmailVerified,
    bool IsActive,
    bool IsLocked,
    DateTime CreatedAt);

public sealed record AdminUserListResponse(
    IReadOnlyList<AdminUserDto> Items,
    int Page,
    int PageSize,
    int Total);
