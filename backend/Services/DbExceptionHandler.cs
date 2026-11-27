using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

/// <summary>
/// Records every unhandled exception in <c>ErrorLogEntries</c> and answers a generic 500 carrying only the
/// trace id. Handled 4xx responses never reach here, so they are not logged.
/// </summary>
public sealed class DbExceptionHandler(
    IServiceScopeFactory scopeFactory,
    IProblemDetailsService problemDetails,
    ILogger<DbExceptionHandler> logger) : IExceptionHandler
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Always to the console too: it is the only record when the database is the thing that failed.
        logger.LogError(exception, "Unhandled exception {TraceId} on {Method} {Path}",
            traceId, httpContext.Request.Method, httpContext.Request.Path);

        try
        {
            await SaveAsync(httpContext, exception, traceId);
        }
        catch (Exception logFailure)
        {
            // The database being down is the likeliest cause of both failures, so the log must not throw twice.
            logger.LogError(logFailure, "Could not write error log row {TraceId}", traceId);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong on our side.",
                Extensions = { ["traceId"] = traceId },
            },
        });
    }

    /// <summary>
    /// A fresh scope, because the request's own context may hold the half-done changes that just failed,
    /// and saving through it would retry them. <c>None</c>: a client that hung up still leaves a row.
    /// </summary>
    private async Task SaveAsync(HttpContext httpContext, Exception exception, string traceId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        // Purge on write keeps the table bounded with no background job. Rows are few, so loading them is cheap.
        var cutoff = now - Retention;
        db.ErrorLogEntries.RemoveRange(
            await db.ErrorLogEntries.Where(entry => entry.OccurredAt < cutoff).ToListAsync(CancellationToken.None));

        db.ErrorLogEntries.Add(new ErrorLogEntry
        {
            OccurredAt = now,
            TraceId = Truncate(traceId, 100),
            Method = Truncate(httpContext.Request.Method, 10),
            // Path only: the query string can carry verification and reset tokens.
            Path = Truncate(httpContext.Request.Path.Value ?? "", 500),
            StatusCode = StatusCodes.Status500InternalServerError,
            ExceptionType = Truncate(exception.GetType().FullName ?? exception.GetType().Name, 300),
            Message = Truncate(exception.Message, 2000),
            StackTrace = Truncate(exception.ToString(), 8000),
            UserId = UserIdOf(httpContext.User),
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Guid? UserIdOf(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var id) ? id : null;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
