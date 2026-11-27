namespace backend.DTOs;

/// <summary>
/// One login as an admin sees it. No token or hash: the session id is the refresh-token family id, which
/// cannot be used to authenticate. <c>IsActive</c> means not ended and seen within one access-token lifetime.
/// </summary>
public sealed record UserSessionDto(
    Guid Id,
    Guid UserId,
    string Email,
    string Name,
    string LoginMethod,
    DateTime StartedAt,
    DateTime LastSeenAt,
    DateTime? EndedAt,
    bool IsActive);

public sealed record UserSessionListResponse(
    IReadOnlyList<UserSessionDto> Items,
    int Page,
    int PageSize,
    int Total);
