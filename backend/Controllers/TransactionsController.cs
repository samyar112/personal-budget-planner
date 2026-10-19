using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>The caller's own ledger. Every query is scoped to the user id in the validated token.</summary>
[ApiController]
[Route("api/transactions")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class TransactionsController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Lists the caller's transactions, newest first. <c>from</c> and <c>to</c> are inclusive dates
    /// (yyyy-MM-dd); <c>category</c> is an exact, case-insensitive match.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<TransactionListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransactionListResponse>> List(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var query = db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId);
        if (from is { } start)
        {
            query = query.Where(transaction => transaction.Date >= start);
        }

        if (to is { } end)
        {
            query = query.Where(transaction => transaction.Date <= end);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim().ToLower();
            query = query.Where(transaction => transaction.Category != null && transaction.Category.ToLower() == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(transaction => transaction.Date)
            .ThenBy(transaction => transaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(transaction => new TransactionResponse(
                transaction.Id,
                transaction.AccountId,
                transaction.Source.ToString(),
                transaction.Date,
                transaction.Description,
                transaction.Amount,
                transaction.Category))
            .ToListAsync(cancellationToken);

        return Ok(new TransactionListResponse(items, page, pageSize, total));
    }
}
