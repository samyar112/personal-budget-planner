using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using backend.DTOs;
using backend.Models;

namespace backend.Services;

/// <summary>
/// The second line of defence behind the allowlist in <see cref="AiPayloadBuilder"/>. It reads the payload
/// exactly as it will be sent and checks every string value. Numbers are not checked: they are typed sums,
/// and a digit rule on them would block anyone whose monthly figures reach six digits. Fail-closed: a single
/// hit on any check means the payload must not be sent.
/// </summary>
public static partial class PayloadPrivacyValidator
{
    public const string SchemaVersion = "1.0";

    /// <summary>Every string the builder may emit, besides ISO dates. Anything else is unknown text.</summary>
    private static readonly HashSet<string> Vocabulary = new(StringComparer.Ordinal)
    {
        SchemaVersion,
        AiPayloadBuilder.Usd, AiPayloadBuilder.Ratio,
        AiPayloadBuilder.IncomeMonthlyMean, AiPayloadBuilder.SpendingMonthlyMean,
        AiPayloadBuilder.NetCashFlowMean, AiPayloadBuilder.SavingsRate,
        // RecurringDetector's cadence names. A new cadence there fails closed here until it is added.
        "weekly", "biweekly", "monthly", "quarterly", "yearly",
    };

    static PayloadPrivacyValidator()
    {
        Vocabulary.UnionWith(TransactionCategory.All);
        Vocabulary.UnionWith(AiPayloadBuilder.AccountTypes);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"(https?://|www\.)|\b[a-z0-9-]+\.(com|net|org|io|co|us|app)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    /// <summary>Ten or more digits with phone separators between them. An ISO date has eight.</summary>
    [GeneratedRegex(@"(?:\d[\s().+-]*){10,}")]
    private static partial Regex Phone();

    /// <summary>The one serialiser for the payload. What is validated, previewed and sent must be the same bytes.</summary>
    public static string Serialize(AiPayload payload) => JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);

    /// <summary>
    /// Runs every check over the payload's string values. <paramref name="identifiers"/> are the user's own
    /// descriptions, account names, institutions, masks, name and email; one appearing as a value is a hit.
    /// </summary>
    public static IReadOnlyList<PrivacyCheck> Validate(AiPayload payload, IEnumerable<string> identifiers)
    {
        var values = new List<string>();
        CollectStrings(JsonNode.Parse(Serialize(payload)), values);

        // Exact match, not contains: a description such as "FOOD" would otherwise match FOOD_AND_DRINK.
        // Fragments of identifiers are caught by the unknown-text and pattern checks instead.
        var own = identifiers
            .Select(identifier => identifier.Trim())
            .Where(identifier => identifier.Length > 0 && !Vocabulary.Contains(identifier))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            Check("digit_run", "Card or account numbers (6+ digits in a row)", values, value => DigitRun().IsMatch(value)),
            Check("email", "Email addresses", values, value => Email().IsMatch(value)),
            Check("url", "Web addresses", values, value => Url().IsMatch(value)),
            Check("phone", "Phone numbers", values, value => Phone().IsMatch(value)),
            Check("user_identifier", "Your descriptions, account names, masks, name or email", values,
                value => own.Contains(value.Trim())),
            Check("unknown_text", "Any text that is not one of our own codes or a date", values,
                value => !Vocabulary.Contains(value) && !IsoDate().IsMatch(value)),
        ];
    }

    private static PrivacyCheck Check(string id, string label, List<string> values, Func<string, bool> hit)
    {
        var hits = values.Count(hit);
        return new PrivacyCheck(id, label, hits, hits == 0);
    }

    private static void CollectStrings(JsonNode? node, List<string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                {
                    CollectStrings(child, values);
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    CollectStrings(child, values);
                }
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                values.Add(value.GetValue<string>());
                break;
        }
    }
}
