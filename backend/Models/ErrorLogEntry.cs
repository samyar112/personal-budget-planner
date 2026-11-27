namespace backend.Models;

/// <summary>
/// One unhandled server exception. Holds the request path but never the query string, headers or body:
/// verification and reset links carry tokens in the query, and bodies can carry ledger data.
/// </summary>
public class ErrorLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>The same id the client receives in the 500 response, so a report can be matched to its row.</summary>
    public required string TraceId { get; set; }

    public required string Method { get; set; }

    public required string Path { get; set; }

    public int StatusCode { get; set; }

    public required string ExceptionType { get; set; }

    public required string Message { get; set; }

    public required string StackTrace { get; set; }

    /// <summary>Null for anonymous requests. Not a foreign key, so a log row outlives a deleted account.</summary>
    public Guid? UserId { get; set; }
}
