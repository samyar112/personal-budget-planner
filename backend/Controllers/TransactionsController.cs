using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
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
    /// Income, expenses and net cash flow for a date range, per account and in total. Internal transfers and
    /// credit card payments are left out (see <see cref="TransactionCategory.CountedInSummaries"/>). The
    /// totals are the sum of the accounts. <c>from</c> and <c>to</c> are inclusive dates (yyyy-MM-dd).
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<TransactionSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransactionSummaryResponse>> Summary(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var counted = db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .Where(TransactionCategory.CountedInSummaries);
        if (from is { } start)
        {
            counted = counted.Where(transaction => transaction.Date >= start);
        }

        if (to is { } end)
        {
            counted = counted.Where(transaction => transaction.Date <= end);
        }

        // Income is decided by category, not sign: a refund is a credit but it is less spending, not income.
        // Rows with no category fall back to sign. Net is every counted amount. Expenses follow as
        // income - net: money out as a positive number, reduced by refunds, so it can go negative.
        var figures = await counted
            .GroupBy(transaction => transaction.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Income = group.Sum(transaction =>
                    transaction.Category == TransactionCategory.Income
                    || (transaction.Category == null && transaction.Amount > 0)
                        ? transaction.Amount
                        : 0m),
                Net = group.Sum(transaction => transaction.Amount),
            })
            .ToDictionaryAsync(figure => figure.AccountId, cancellationToken);

        var accountIds = await db.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.CreatedAt)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);

        var accounts = accountIds
            .Select(id => figures.TryGetValue(id, out var figure)
                ? new AccountSummaryResponse(id, figure.Income, figure.Income - figure.Net, figure.Net)
                : new AccountSummaryResponse(id, 0m, 0m, 0m))
            .ToList();

        return Ok(new TransactionSummaryResponse(
            accounts.Sum(account => account.Income),
            accounts.Sum(account => account.Expenses),
            accounts.Sum(account => account.Net),
            accounts));
    }

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
                transaction.Category,
                transaction.Subcategory))
            .ToListAsync(cancellationToken);

        return Ok(new TransactionListResponse(items, page, pageSize, total));
    }
}
