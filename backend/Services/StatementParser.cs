using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace backend.Services;

/// <summary>One transaction line read from a statement. Amount uses the ledger sign: negative is money out.</summary>
public sealed record StatementLine(DateOnly Date, string Description, decimal Amount);

/// <summary>A parsed statement: the account's last four digits and its transaction lines, in statement order.</summary>
public sealed record ParsedStatement(string Mask, IReadOnlyList<StatementLine> Lines);

/// <summary>Thrown when a file is not a statement this parser supports. The message is safe to show the user.</summary>
public sealed class StatementRejectedException(string message) : Exception(message);

/// <summary>
/// Reads the one supported statement layout: an "Account ending in 1234" line, then a table headed
/// "Date Description Amount" whose rows are <c>MM/dd/yyyy</c>, free text, and a signed amount such as
/// <c>-1,234.56</c>. Anything else is rejected rather than guessed at. Lines after the header that do not
/// start with a date (page footers, totals) are skipped; a line that starts with a date but does not parse
/// rejects the whole file, so a half-read statement never reaches the ledger.
/// The file is read in memory and never stored; only the lines and the last four digits leave this class.
/// </summary>
public static partial class StatementParser
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPages = 50;
    public const int MaxDescriptionLength = 500;

    public const string UnrecognisedMessage =
        "This statement layout is not supported. Upload a statement with an \"Account ending in\" line and a Date, Description, Amount table.";

    public static ParsedStatement Parse(Stream pdf)
    {
        using var buffer = new MemoryStream();
        pdf.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            throw new StatementRejectedException($"The file must be a PDF of at most {MaxBytes / (1024 * 1024)} MB.");
        }

        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
        {
            throw new StatementRejectedException("The file is not a PDF.");
        }

        List<string> text;
        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages > MaxPages)
            {
                throw new StatementRejectedException($"The statement has more than {MaxPages} pages.");
            }

            text = document.GetPages().SelectMany(TextLines).ToList();
        }
        catch (StatementRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            // Malformed, encrypted or otherwise unreadable. PdfPig's exception text is not shown to the user.
            throw new StatementRejectedException("The PDF could not be read. It may be damaged or password-protected.");
        }

        return ParseText(text);
    }

    /// <summary>The layout rules, applied to the statement's text lines in reading order.</summary>
    public static ParsedStatement ParseText(IReadOnlyList<string> text)
    {
        string? mask = null;
        var inTable = false;
        var lines = new List<StatementLine>();
        foreach (var raw in text)
        {
            var line = Whitespace().Replace(raw, " ").Trim();
            if (mask is null && AccountEnding().Match(line) is { Success: true } ending)
            {
                mask = ending.Groups[1].Value;
                continue;
            }

            if (Header().IsMatch(line))
            {
                // The header repeats on every page.
                inTable = true;
                continue;
            }

            if (!inTable || !StartsWithDate().IsMatch(line))
            {
                continue;
            }

            var row = Row().Match(line);
            if (!row.Success
                || !DateOnly.TryParseExact(row.Groups["date"].Value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(row.Groups["amount"].Value.Replace("$", "").Replace(",", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            {
                throw new StatementRejectedException(UnrecognisedMessage);
            }

            var description = row.Groups["description"].Value.Trim();
            if (description.Length > MaxDescriptionLength)
            {
                description = description[..MaxDescriptionLength];
            }

            lines.Add(new StatementLine(date, description, amount));
        }

        if (mask is null || lines.Count == 0)
        {
            throw new StatementRejectedException(UnrecognisedMessage);
        }

        return new ParsedStatement(mask, lines);
    }

    /// <summary>Groups a page's words into lines by baseline, top to bottom, each read left to right.</summary>
    private static IEnumerable<string> TextLines(Page page) =>
        page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom))
            .OrderByDescending(group => group.Key)
            .Select(group => string.Join(' ', group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\bAccount\s+ending\s+in\s+(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountEnding();

    [GeneratedRegex(@"^Date Description Amount$", RegexOptions.IgnoreCase)]
    private static partial Regex Header();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}\b")]
    private static partial Regex StartsWithDate();

    [GeneratedRegex(@"^(?<date>\d{2}/\d{2}/\d{4}) (?<description>.+) (?<amount>-?\$?\d{1,3}(?:,\d{3})*\.\d{2})$")]
    private static partial Regex Row();
}
