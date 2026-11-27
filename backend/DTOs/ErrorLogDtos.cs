namespace backend.DTOs;

/// <summary>
/// One logged server error as an admin sees it. Carries the path only; the query string was never stored.
/// </summary>
public sealed record ErrorLogEntryDto(
    Guid Id,
    DateTime OccurredAt,
    string TraceId,
    string Method,
    string Path,
    int StatusCode,
    string ExceptionType,
    string Message,
    string StackTrace,
    Guid? UserId);

public sealed record ErrorLogListResponse(
    IReadOnlyList<ErrorLogEntryDto> Items,
    int Page,
    int PageSize,
    int Total);
