using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>The caller's own ledger. Every query is scoped to the user id in the validated token.</summary>
[ApiController]
[Route("api/transactions")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class TransactionsController(AppDbContext db, HealthScoreService healthScore) : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxMonths = 24;
    private const int MaxMerchants = 50;
    private const int RecurringWindowMonths = 13;

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
            .Where(transaction => transaction.Account.IncludeInTotals)
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
            .Where(account => account.UserId == userId && account.IncludeInTotals)
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
    /// Spending by category for a date range, largest first. Same rows as the summary; refunds net against
    /// their own category, and rows with no category are OTHER. <c>accountId</c> narrows to one account.
    /// </summary>
    [HttpGet("spending/categories")]
    [ProducesResponseType<SpendingByCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpendingByCategoryResponse>> SpendingByCategory(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? accountId = null)
    {
        var (rows, error) = await CountedRowsAsync(from, to, accountId, cancellationToken);
        if (rows is null)
        {
            return error!;
        }

        var categories = await rows
            .Where(TransactionCategory.IsSpending)
            .GroupBy(transaction => transaction.Category ?? TransactionCategory.Other)
            .Select(group => new CategorySpendingResponse(group.Key, -group.Sum(transaction => transaction.Amount)))
            .ToListAsync(cancellationToken);

        var ordered = categories.OrderByDescending(category => category.Amount).ToList();
        return Ok(new SpendingByCategoryResponse(ordered.Sum(category => category.Amount), ordered));
    }

    /// <summary>
    /// Income, expenses and net cash flow for each calendar month from <c>from</c> to <c>to</c>, oldest
    /// first, by the same rules as the summary. Both dates are required and the range is capped at
    /// <see cref="MaxMonths"/> months.
    /// </summary>
    [HttpGet("spending/monthly")]
    [ProducesResponseType<MonthlyCashFlowListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonthlyCashFlowListResponse>> MonthlyCashFlow(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? accountId = null)
    {
        if (from is not { } start || to is not { } end || start > end
            || (end.Year - start.Year) * 12 + end.Month - start.Month >= MaxMonths)
        {
            ModelState.AddModelError("range", $"from and to are required, from must not be after to, and the range is at most {MaxMonths} months.");
            return ValidationProblem(ModelState);
        }

        var (rows, error) = await CountedRowsAsync(start, end, accountId, cancellationToken);
        if (rows is null)
        {
            return error!;
        }

        // Same income rule as Summary: by category, or by sign when there is no category.
        var figures = await rows
            .GroupBy(transaction => new { transaction.Date.Year, transaction.Date.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Income = group.Sum(transaction =>
                    transaction.Category == TransactionCategory.Income
                    || (transaction.Category == null && transaction.Amount > 0)
                        ? transaction.Amount
                        : 0m),
                Net = group.Sum(transaction => transaction.Amount),
            })
            .ToDictionaryAsync(figure => (figure.Year, figure.Month), cancellationToken);

        var months = new List<MonthlyCashFlowResponse>();
        for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
        {
            months.Add(figures.TryGetValue((month.Year, month.Month), out var figure)
                ? new MonthlyCashFlowResponse(month.Year, month.Month, figure.Income, figure.Income - figure.Net, figure.Net)
                : new MonthlyCashFlowResponse(month.Year, month.Month, 0m, 0m, 0m));
        }

        return Ok(new MonthlyCashFlowListResponse(months));
    }

    /// <summary>
    /// The largest payees by spending, net of refunds, keyed by the stored description. <c>category</c> is an
    /// exact category code; <c>limit</c> is 1 to <see cref="MaxMerchants"/>.
    /// </summary>
    [HttpGet("spending/merchants")]
    [ProducesResponseType<TopMerchantsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TopMerchantsResponse>> TopMerchants(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] string? category = null,
        [FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 1, MaxMerchants);

        var (rows, error) = await CountedRowsAsync(from, to, accountId, cancellationToken);
        if (rows is null)
        {
            return error!;
        }

        var spending = rows.Where(TransactionCategory.IsSpending);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim().ToUpperInvariant();
            spending = wanted == TransactionCategory.Other
                ? spending.Where(transaction => transaction.Category == null || transaction.Category == wanted)
                : spending.Where(transaction => transaction.Category == wanted);
        }

        var merchants = await spending
            .GroupBy(transaction => transaction.Description)
            .Select(group => new
            {
                Merchant = group.Key,
                Amount = -group.Sum(transaction => transaction.Amount),
                Count = group.Count(),
            })
            .Where(merchant => merchant.Amount > 0)
            .OrderByDescending(merchant => merchant.Amount)
            .ThenBy(merchant => merchant.Merchant)
            .Take(limit)
            .Select(merchant => new MerchantSpendingResponse(merchant.Merchant, merchant.Amount, merchant.Count))
            .ToListAsync(cancellationToken);

        return Ok(new TopMerchantsResponse(merchants));
    }

    /// <summary>
    /// Repeating charges found in the last <see cref="RecurringWindowMonths"/> months of spending, soonest
    /// next charge first. Same counted rows as the summary, money out only: income, own-account transfers,
    /// card payments and refunds never become bills. See <see cref="RecurringDetector"/> for the rules.
    /// </summary>
    [HttpGet("recurring")]
    [ProducesResponseType<RecurringListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RecurringListResponse>> Recurring(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (rows, error) = await CountedRowsAsync(today.AddMonths(-RecurringWindowMonths), today, null, cancellationToken);
        if (rows is null)
        {
            return error!;
        }

        var charges = await rows
            .Where(TransactionCategory.IsSpending)
            .Where(transaction => transaction.Amount < 0)
            .Select(transaction => new RecurringCandidate(
                transaction.Date, transaction.Description, -transaction.Amount, transaction.Category))
            .ToListAsync(cancellationToken);

        var streams = RecurringDetector.Detect(charges, today)
            .Select(stream => new RecurringStreamResponse(
                stream.Merchant,
                stream.Category,
                stream.Cadence,
                stream.TypicalAmount,
                stream.LastAmount,
                stream.LastDate,
                stream.NextDate,
                stream.Occurrences,
                stream.MonthlyCost,
                stream.IsSubscription))
            .ToList();

        return Ok(new RecurringListResponse(streams.Sum(stream => stream.MonthlyCost), streams));
    }

    /// <summary>
    /// The financial health score, 0–100, from eight rule-based indicators in four components. Computed on
    /// every read from the caller's own accounts and counted rows; nothing here is sent to an AI provider.
    /// See <see cref="HealthScore"/> for the rules.
    /// </summary>
    [HttpGet("health-score")]
    [ProducesResponseType<HealthScoreResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HealthScoreResponse>> Health(CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        return Ok(await healthScore.ComputeAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));
    }

    /// <summary>
    /// The caller's rows that summaries count, within the dates and, when given, one account. The account
    /// must be the caller's: someone else's id is 404, the same as one that does not exist. Without an account,
    /// statement accounts (<see cref="Account.IncludeInTotals"/> false) are left out; naming one is how the
    /// statement view reuses these figures.
    /// </summary>
    private async Task<(IQueryable<Transaction>? Rows, ActionResult? Error)> CountedRowsAsync(
        DateOnly? from, DateOnly? to, Guid? accountId, CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return (null, Unauthorized());
        }

        var rows = db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .Where(TransactionCategory.CountedInSummaries);

        if (accountId is { } account)
        {
            var owned = await db.Accounts.AnyAsync(
                candidate => candidate.Id == account && candidate.UserId == userId, cancellationToken);
            if (!owned)
            {
                return (null, NotFound());
            }

            rows = rows.Where(transaction => transaction.AccountId == account);
        }
        else
        {
            rows = rows.Where(transaction => transaction.Account.IncludeInTotals);
        }

        if (from is { } start)
        {
            rows = rows.Where(transaction => transaction.Date >= start);
        }

        if (to is { } end)
        {
            rows = rows.Where(transaction => transaction.Date <= end);
        }

        return (rows, null);
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

        // Statement rows have their own view until they are merged.
        var query = db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .Where(transaction => transaction.Account.IncludeInTotals);
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
