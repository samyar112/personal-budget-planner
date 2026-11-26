using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using backend.DTOs;
using backend.Models;

namespace backend.Services;

/// <summary>
/// A tip the rules chose, with template wording and every number that wording is allowed to contain. The rules
/// decide the advice; the model may only reword it. Built from the payload alone, so it carries nothing the
/// privacy check has not already passed.
/// </summary>
public sealed record TipCandidate(
    string Id,
    string Title,
    string Text,
    IReadOnlyList<decimal> Numbers,
    IReadOnlyList<RoadmapStep>? Steps = null)
{
    public IReadOnlyList<RoadmapStep> Steps { get; init; } = Steps ?? [];
}

/// <summary>
/// The rules, the prompt and the check on the model's reply. Pure functions over the payload, so each can be
/// tested without a database or a network.
/// </summary>
public static partial class AiTips
{
    public const string PromptVersion = "tips.v1";
    public const string SourceAi = "ai";
    public const string SourceTemplate = "template";
    public const string SourceMixed = "mixed";
    public const int MaxTips = 5;
    public const int MinTips = 3;
    public const int MaxTitleLength = 80;
    public const int MaxTextLength = 300;

    public const string NetNegative = "NET_NEGATIVE";
    public const string EmergencyBuffer = "EMERGENCY_BUFFER";
    public const string SavingsRate = "SAVINGS_RATE";
    public const string FlexibleCategory = "FLEXIBLE_CATEGORY";
    public const string Subscriptions = "SUBSCRIPTIONS";
    public const string FixedBills = "FIXED_BILLS";

    public const string SystemPrompt =
        """
        You reword short personal-finance tips. You are given a JSON object with "payload" (a person's monthly
        figures) and "candidates" (tips already chosen by rules, each with an id, a title and a text).
        - Choose 3 to 5 candidates, most important first. Keep the given order unless another order is clearly better.
        - Rewrite each title and text in plain, friendly language, second person, no jargon.
        - Use only numbers that appear in that candidate's text. Do not calculate new figures, totals or percentages.
        - Do not add advice that is not in the candidate. No investment, product, tax or legal advice.
          Do not promise outcomes. You are not a licensed financial adviser and must not claim to be.
        - Title at most 80 characters, text at most 300 characters.
        Reply with JSON only: {"tips":[{"candidateId":"...","title":"...","text":"..."}]}
        """;

    /// <summary>The reply's JSON schema, sent to providers that enforce structured output.</summary>
    public const string ReplySchema =
        """
        {
          "type": "object",
          "properties": {
            "tips": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "candidateId": { "type": "string" },
                  "title": { "type": "string" },
                  "text": { "type": "string" }
                },
                "required": ["candidateId", "title", "text"],
                "additionalProperties": false
              }
            }
          },
          "required": ["tips"],
          "additionalProperties": false
        }
        """;

    /// <summary>Digits with optional thousands separators and decimals: 1,234.56 or 12.3 or 20.</summary>
    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(
        @"\b(guarantee[sd]?|stocks?|crypto\w*|bitcoin|etfs?|index funds?|ticker|invest(?:ing|ment)? in|tax(?:es)?|legal|lawyer|insurance|loan offers?|urgent(?:ly)?|immediately|act now)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Prohibited();

    /// <summary>
    /// Categories a person can trim month to month. Rent, utilities, medical costs, loan payments and fees are
    /// left out: telling someone to spend 10% less on rent is not advice they can act on this month.
    /// </summary>
    public static readonly IReadOnlySet<string> Flexible = new HashSet<string>(StringComparer.Ordinal)
    {
        TransactionCategory.FoodAndDrink,
        TransactionCategory.Entertainment,
        TransactionCategory.GeneralMerchandise,
        TransactionCategory.Travel,
        TransactionCategory.PersonalCare,
    };

    /// <summary>
    /// Every rule that applies, most important first: spending above income, then a cash buffer, then the savings
    /// rate, then where the money goes. Each tip is a monthly step sized from the person's own figures, so it is
    /// something they can start this month. The order follows the research doc's hierarchy and is a design choice.
    /// </summary>
    public static IReadOnlyList<TipCandidate> Candidates(AiPayload payload)
    {
        var income = Metric(payload, AiPayloadBuilder.IncomeMonthlyMean);
        var spending = Metric(payload, AiPayloadBuilder.SpendingMonthlyMean);
        var net = Metric(payload, AiPayloadBuilder.NetCashFlowMean);
        var rate = Metric(payload, AiPayloadBuilder.SavingsRate);
        var flexible = payload.Categories.FirstOrDefault(category => Flexible.Contains(category.Code));
        var candidates = new List<TipCandidate>();

        // With no income in the linked accounts, "spending is above income" says nothing useful: the gap is
        // simply all spending. DataGaps says so instead.
        var gapTip = net < 0m && income > 0m;
        if (gapTip)
        {
            var gap = -net.Value;
            if (flexible is null)
            {
                candidates.Add(new TipCandidate(NetNegative, "Spending is above income",
                    $"On average you spend {Money(gap)} more than you earn each month. Start with your largest costs.",
                    [gap]));
            }
            else
            {
                // A 15% trim of the largest flexible category: noticeable, but not a lifestyle overhaul.
                var cut = Round(flexible.MonthlyMean * 0.15m);
                var covers = cut >= gap
                    ? "enough to close the gap."
                    : $"covering {Math.Round(cut / gap * 100m, 0, MidpointRounding.AwayFromZero)}% of the gap.";
                // When the cut more than closes the gap, the roadmap builds a buffer from what it frees.
                IReadOnlyList<RoadmapStep> steps = cut > gap
                    ? [new RoadmapStep($"Spend 15% less on {Label(flexible.Code)}", cut, PerMonth, null),
                        .. BufferSteps(spending!.Value, Round((cut - gap) * 0.5m), payload.AsOf)]
                    : [];
                candidates.Add(new TipCandidate(NetNegative, "Spending is above income",
                    $"On average you spend {Money(gap)} more than you earn each month. Spending 15% less on "
                    + $"{Label(flexible.Code)} ({Money(flexible.MonthlyMean)} a month now) would save about "
                    + $"{Money(cut)}, {covers}",
                    [gap, 15m, flexible.MonthlyMean, cut, Math.Round(cut / gap * 100m, 0, MidpointRounding.AwayFromZero)],
                    steps));
            }
        }

        if (net > 0m && spending > 0m)
        {
            // Half the surplus: leaves room for the irregular costs a monthly average hides.
            var monthly = Round(net.Value * 0.5m);
            var months = (int)Math.Ceiling(spending.Value / monthly);
            candidates.Add(new TipCandidate(EmergencyBuffer, "Build a cash buffer",
                $"Putting aside {Money(monthly)} a month, half of what you have left over, builds one month of "
                + $"spending ({Money(spending.Value)}) in about {months} months.",
                [monthly, spending.Value, months],
                BufferSteps(spending.Value, monthly, payload.AsOf)));
        }

        if (rate >= 0m && net >= 0m && income > 0m)
        {
            var percent = Math.Round(rate.Value * 100m, 1, MidpointRounding.AwayFromZero);
            if (rate < 0.2m)
            {
                // Five points at a time, capped at the common 20% guideline, rather than one jump to 20%.
                var target = Math.Min(rate.Value + 0.05m, 0.2m);
                var targetPercent = Math.Round(target * 100m, 1, MidpointRounding.AwayFromZero);
                var extra = Round(income.Value * target - net.Value);
                candidates.Add(new TipCandidate(SavingsRate, "Raise your savings rate",
                    $"You save about {percent}% of your income. Raising it to {targetPercent}% means saving about "
                    + $"{Money(extra)} more each month.",
                    [percent, targetPercent, extra],
                    SavingsSteps(income.Value, net.Value, rate.Value)));
            }
            else
            {
                candidates.Add(new TipCandidate(SavingsRate, "Keep your savings rate",
                    $"You save about {percent}% of your income, above the common 20% guideline. Keep it steady.",
                    [percent, 20m]));
            }
        }

        // The gap tip already names this category; a second tip about it would repeat the first.
        if (flexible is not null && !gapTip)
        {
            var cut = Round(flexible.MonthlyMean * 0.1m);
            candidates.Add(new TipCandidate(FlexibleCategory, "Trim your largest flexible cost",
                $"{Sentence(Label(flexible.Code))} is your largest flexible cost at about {Money(flexible.MonthlyMean)} "
                + $"a month. Spending 10% less would free up about {Money(cut)} a month.",
                [flexible.MonthlyMean, 10m, cut]));
        }

        var subscriptions = payload.Recurring.Where(stream => stream.IsSubscription).ToList();
        if (subscriptions.Count > 0)
        {
            var cost = Round(subscriptions.Sum(stream => stream.MonthlyCost));
            candidates.Add(new TipCandidate(Subscriptions, "Review your subscriptions",
                $"You have {subscriptions.Count} subscription{(subscriptions.Count == 1 ? "" : "s")} costing about "
                + $"{Money(cost)} a month. Cancelling ones you rarely use saves that money every month.",
                [subscriptions.Count, cost]));
        }

        var bills = payload.Recurring.Where(stream => !stream.IsSubscription).ToList();
        if (bills.Count > 0 && spending > 0m)
        {
            var cost = Round(bills.Sum(stream => stream.MonthlyCost));
            var share = Math.Round(cost / spending.Value * 100m, 1, MidpointRounding.AwayFromZero);
            candidates.Add(new TipCandidate(FixedBills, "Check your regular bills",
                $"Regular bills come to about {Money(cost)} a month, {share}% of your spending. Comparing providers "
                + "once a year can lower them.",
                [cost, share]));
        }

        return candidates;
    }

    public const string PerMonth = "month";
    public const string PerTotal = "total";

    /// <summary>Milestones further out than this are left off: a 40-year plan is not one anyone follows.</summary>
    private const int MaxRoadmapMonths = 120;

    private static readonly int[] BufferMilestones = [1, 3, 6];

    /// <summary>
    /// Save <paramref name="monthly"/> toward 1, 3 and 6 months of spending, each with the month it is reached
    /// if today's average holds. Counted from the start of the payload's month.
    /// </summary>
    private static List<RoadmapStep> BufferSteps(decimal spending, decimal monthly, DateOnly asOf)
    {
        if (monthly <= 0m)
        {
            return [];
        }

        var start = new DateOnly(asOf.Year, asOf.Month, 1);
        var steps = new List<RoadmapStep> { new("Save each month", monthly, PerMonth, null) };
        foreach (var multiple in BufferMilestones)
        {
            var target = Round(spending * multiple);
            var months = (int)Math.Ceiling(target / monthly);
            if (months > MaxRoadmapMonths)
            {
                break;
            }

            steps.Add(new RoadmapStep(
                $"{multiple} month{(multiple == 1 ? "" : "s")} of spending", target, PerTotal, start.AddMonths(months)));
        }

        return steps;
    }

    /// <summary>Five points of income at a time up to 20%, with the extra saved each month at each step.</summary>
    private static List<RoadmapStep> SavingsSteps(decimal income, decimal net, decimal rate)
    {
        var steps = new List<RoadmapStep>();
        for (var target = rate + 0.05m; ; target += 0.05m)
        {
            var capped = Math.Min(target, 0.2m);
            var percent = Math.Round(capped * 100m, 1, MidpointRounding.AwayFromZero);
            steps.Add(new RoadmapStep($"Save {percent:0.0}% of income", Round(income * capped - net), PerMonth, null));
            if (capped >= 0.2m)
            {
                return steps;
            }
        }
    }

    /// <summary>What the tips cannot cover, said plainly instead of guessed at.</summary>
    public static IReadOnlyList<string> DataGaps(AiPayload payload)
    {
        var months = payload.Coverage.MonthsComplete;
        var gaps = new List<string>();
        if (months == 0)
        {
            gaps.Add("There is no complete month of data yet. Tips will appear once a full month has synced.");
        }
        else if (months < 3)
        {
            gaps.Add($"Tips use {months} complete month{(months == 1 ? "" : "s")} of data, so the averages may still shift.");
        }

        if (months > 0 && !(Metric(payload, AiPayloadBuilder.IncomeMonthlyMean) > 0m))
        {
            gaps.Add("No income was found in your linked accounts, so tips can't compare spending with income.");
        }

        gaps.Add("Account balances and debt interest rates are never shared, so tips do not cover them.");
        return gaps;
    }

    /// <summary>The template wording of the top candidates: the answer when the model is off, down or wrong.</summary>
    public static IReadOnlyList<AiTip> Templates(IReadOnlyList<TipCandidate> candidates) =>
        candidates.Take(MaxTips).Select(Template).ToList();

    private static AiTip Template(TipCandidate candidate) =>
        new(candidate.Id, candidate.Title, candidate.Text, SourceTemplate, candidate.Steps);

    /// <summary>
    /// The model's checked tips in the model's order, topped up with template wording for the highest-ranked
    /// candidates it did not cover until there are <see cref="MinTips"/> (or every candidate, if fewer). One tip
    /// the check rejects costs only that tip's wording, not the whole reply.
    /// </summary>
    public static IReadOnlyList<AiTip> Combine(IReadOnlyList<AiTip> validated, IReadOnlyList<TipCandidate> candidates)
    {
        var tips = validated.Take(MaxTips).ToList();
        var wanted = Math.Min(MinTips, candidates.Count);
        foreach (var candidate in candidates)
        {
            if (tips.Count >= wanted)
            {
                break;
            }

            if (tips.All(tip => tip.Id != candidate.Id))
            {
                tips.Add(Template(candidate));
            }
        }

        return tips;
    }

    /// <summary>
    /// The model's reply checked tip by tip, failing closed. A tip survives only if it names a candidate not
    /// already used, fits the length limits, uses no number its candidate does not hold and has no prohibited
    /// wording. Returns the survivors, possibly none; <see cref="Combine"/> fills the rest from templates.
    /// </summary>
    public static IReadOnlyList<AiTip> Validate(string reply, IReadOnlyList<TipCandidate> candidates)
    {
        var byId = candidates.ToDictionary(candidate => candidate.Id);
        var tips = new List<AiTip>();

        try
        {
            using var document = JsonDocument.Parse(StripCodeFence(reply));
            if (!document.RootElement.TryGetProperty("tips", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryString(item, "candidateId", out var id)
                    || !TryString(item, "title", out var title)
                    || !TryString(item, "text", out var text)
                    || !byId.TryGetValue(id, out var candidate)
                    || tips.Any(tip => tip.Id == id)
                    || title.Length > MaxTitleLength
                    || text.Length > MaxTextLength
                    || Prohibited().IsMatch(title)
                    || Prohibited().IsMatch(text)
                    || !NumbersAllowed(title + " " + text, candidate.Numbers))
                {
                    continue;
                }

                tips.Add(new AiTip(id, title.Trim(), text.Trim(), SourceAi, candidate.Steps));
                if (tips.Count == MaxTips)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return tips;
    }

    /// <summary>
    /// Every number written in <paramref name="text"/> must equal an allowed value, either exactly or rounded to
    /// a whole number or one decimal, so "$412" for 412.35 passes and "$500" does not.
    /// </summary>
    public static bool NumbersAllowed(string text, IReadOnlyList<decimal> allowed)
    {
        foreach (Match match in Number().Matches(text))
        {
            var value = decimal.Parse(match.Value.Replace(",", ""), CultureInfo.InvariantCulture);
            var known = allowed.Any(number =>
                value == number
                || value == Math.Round(number, 0, MidpointRounding.AwayFromZero)
                || value == Math.Round(number, 1, MidpointRounding.AwayFromZero));
            if (!known)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryString(JsonElement item, string name, out string value)
    {
        value = "";
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return value.Trim().Length > 0;
    }

    /// <summary>Some models wrap JSON in a markdown code fence even when asked not to.</summary>
    private static string StripCodeFence(string reply)
    {
        var text = reply.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && lastFence > firstLine ? text[(firstLine + 1)..lastFence] : text;
    }

    private static decimal? Metric(AiPayload payload, string id) =>
        payload.Metrics.FirstOrDefault(metric => metric.Id == id)?.Value;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Money(decimal value) => "$" + Round(value).ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>FOOD_AND_DRINK reads as "food and drink".</summary>
    private static string Label(string code) => code.Replace('_', ' ').ToLowerInvariant();

    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
