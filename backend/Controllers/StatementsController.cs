using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>
/// PDF statement upload and the separate statement view. Uploaded rows land in the ledger as
/// <see cref="TransactionSource.PdfUpload"/> on an account with <see cref="Account.IncludeInTotals"/> false,
/// so they never reach totals, charts, recurring detection, the health score or the AI payload.
/// The PDF itself is parsed in memory and never stored.
/// </summary>
[ApiController]
[Route("api/statements")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class StatementsController(AppDbContext db) : ControllerBase
{
    // Multipart framing on top of the file itself.
    private const long MaxRequestBytes = StatementParser.MaxBytes + 64 * 1024;

    /// <summary>
    /// Parses one statement and adds its lines to the statement account for its last four digits, creating
    /// that account on first upload. Lines already imported are skipped. 400 for anything that is not a PDF
    /// in the supported layout; nothing is written then.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<StatementUploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<StatementUploadResponse>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        if (file is null || file.Length == 0 || file.Length > StatementParser.MaxBytes)
        {
            ModelState.AddModelError("file", $"Choose a PDF of at most {StatementParser.MaxBytes / (1024 * 1024)} MB.");
            return ValidationProblem(ModelState);
        }

        ParsedStatement statement;
        try
        {
            await using var stream = file.OpenReadStream();
            statement = StatementParser.Parse(stream);
        }
        catch (StatementRejectedException rejected)
        {
            ModelState.AddModelError("file", rejected.Message);
            return ValidationProblem(ModelState);
        }

        var account = await db.Accounts.FirstOrDefaultAsync(
            candidate => candidate.UserId == userId
                && candidate.PlaidItemId == null
                && !candidate.IncludeInTotals
                && candidate.Mask == statement.Mask,
            cancellationToken);
        if (account is null)
        {
            account = new Account
            {
                UserId = userId,
                Name = $"Statement ending {statement.Mask}",
                Mask = statement.Mask,
                IncludeInTotals = false,
            };
            db.Accounts.Add(account);
        }

        var candidates = Rows(userId, account, statement);
        var externalIds = candidates.Select(row => row.ExternalId).ToList();
        var existing = await db.Transactions
            .Where(transaction => transaction.UserId == userId
                && transaction.Source == TransactionSource.PdfUpload
                && externalIds.Contains(transaction.ExternalId))
            .Select(transaction => transaction.ExternalId)
            .ToListAsync(cancellationToken);
        var fresh = candidates.Where(row => !existing.Contains(row.ExternalId)).ToList();

        db.Transactions.AddRange(fresh);
        await db.SaveChangesAsync(cancellationToken);

        return Created(
            $"/api/statements/{account.Id}",
            new StatementUploadResponse(account.Id, fresh.Count, candidates.Count - fresh.Count));
    }

    /// <summary>The caller's statement accounts, oldest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StatementAccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<StatementAccountResponse>>> List(CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        var accounts = await StatementAccounts(userId)
            .OrderBy(account => account.CreatedAt)
            .Select(account => new StatementAccountResponse(
                account.Id,
                account.Name,
                account.Mask,
                db.Transactions.Count(transaction => transaction.AccountId == account.Id),
                db.Transactions.Where(transaction => transaction.AccountId == account.Id).Min(transaction => (DateOnly?)transaction.Date),
                db.Transactions.Where(transaction => transaction.AccountId == account.Id).Max(transaction => (DateOnly?)transaction.Date)))
            .ToListAsync(cancellationToken);

        return Ok(accounts);
    }

    /// <summary>
    /// One statement account's figures and rows. Someone else's account, or one that is not a statement
    /// account, is 404, the same as one that does not exist.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatementDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StatementDetailResponse>> Detail(Guid id, CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        var account = await StatementAccounts(userId).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId && transaction.AccountId == id)
            .OrderByDescending(transaction => transaction.Date)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        // Same rules as the main summary: transfers and card payments left out; income by category, or by
        // sign when there is none; expenses are income minus net.
        var counted = rows.Where(TransactionCategory.CountedInSummaries.Compile()).ToList();
        var income = counted
            .Where(row => row.Category == TransactionCategory.Income || (row.Category == null && row.Amount > 0))
            .Sum(row => row.Amount);
        var net = counted.Sum(row => row.Amount);

        return Ok(new StatementDetailResponse(
            new StatementAccountResponse(
                account.Id,
                account.Name,
                account.Mask,
                rows.Count,
                rows.Count == 0 ? null : rows.Min(row => row.Date),
                rows.Count == 0 ? null : rows.Max(row => row.Date)),
            income,
            income - net,
            net,
            rows.Select(row => new TransactionResponse(
                    row.Id, row.AccountId, row.Source.ToString(), row.Date, row.Description, row.Amount, row.Category, row.Subcategory))
                .ToList()));
    }

    private IQueryable<Account> StatementAccounts(Guid userId) =>
        db.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId && account.PlaidItemId == null && !account.IncludeInTotals);

    /// <summary>
    /// Ledger rows for a statement. The external id hashes the account's mask, the line, and how many identical
    /// lines came before it in this statement, so a re-upload matches every row again while two real
    /// same-day, same-amount purchases stay two rows.
    /// </summary>
    private static List<Transaction> Rows(Guid userId, Account account, ParsedStatement statement)
    {
        var seen = new Dictionary<StatementLine, int>();
        var rows = new List<Transaction>();
        foreach (var line in statement.Lines)
        {
            var occurrence = seen.GetValueOrDefault(line);
            seen[line] = occurrence + 1;

            var key = $"{statement.Mask}|{line.Date:yyyy-MM-dd}|{line.Amount:0.00}|{line.Description}|{occurrence}";
            var categorization = TransactionCategorizer.Categorize(null, null, null, line.Description, line.Amount);
            rows.Add(new Transaction
            {
                UserId = userId,
                Account = account,
                AccountId = account.Id,
                Source = TransactionSource.PdfUpload,
                ExternalId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))),
                Date = line.Date,
                Description = line.Description,
                Amount = line.Amount,
                Category = categorization.Category,
                Subcategory = categorization.Subcategory,
            });
        }

        return rows;
    }

    private Guid? CallerId()
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
