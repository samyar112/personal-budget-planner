namespace backend.DTOs;

/// <summary>
/// The financial health score, modelled on the Financial Health Network's FinHealth framework: four
/// components (Spend, Save, Borrow, Plan) of two indicators each, every indicator 0–100. The score is the mean
/// of the indicators that have data, so a missing account lowers coverage, never the score. <c>Score</c> and
/// <c>Tier</c> are null until there is income in at least one complete month.
/// </summary>
public sealed record HealthScoreResponse(
    int? Score,
    string? Tier,
    int IndicatorsScored,
    int IndicatorsTotal,
    int MonthsComplete,
    IReadOnlyList<HealthComponent> Components);

/// <summary>One of Spend, Save, Borrow or Plan. <c>Score</c> is the mean of its indicators that have data.</summary>
public sealed record HealthComponent(string Id, string Name, int? Score, IReadOnlyList<HealthIndicator> Indicators);

/// <summary>
/// One indicator. <c>Score</c> is null when the data it needs is missing, and <c>Reason</c> says what is
/// missing. <c>Method</c> is the rule itself, shown in the legend, so the explanation and the arithmetic
/// cannot drift apart. <c>Estimated</c> marks a ledger stand-in for what the framework asks in a survey.
/// </summary>
public sealed record HealthIndicator(
    string Id,
    string Name,
    int? Score,
    string Reason,
    string Method,
    bool Estimated);
