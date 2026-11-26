namespace backend.DTOs;

/// <summary>
/// The only thing that may leave the API for an AI provider. Built from an allowlist: typed numbers, dates,
/// and our own codes. No descriptions, merchant names, account names, institutions, masks or balances.
/// </summary>
public sealed record AiPayload(
    string SchemaVersion,
    DateOnly AsOf,
    PayloadCoverage Coverage,
    IReadOnlyList<PayloadMetric> Metrics,
    IReadOnlyList<PayloadCategory> Categories,
    IReadOnlyList<PayloadRecurring> Recurring);

/// <summary>Which kinds of account the figures come from, and how many complete months they average over.</summary>
public sealed record PayloadCoverage(IReadOnlyList<string> AccountTypes, int MonthsComplete);

/// <summary>One computed figure. <c>Value</c> is null when there is not enough data to compute it.</summary>
public sealed record PayloadMetric(string Id, decimal? Value, string Unit);

/// <summary>Average monthly spending in one category, net of refunds.</summary>
public sealed record PayloadCategory(string Code, decimal MonthlyMean);

/// <summary>A repeating charge as category, cadence and cost only. The merchant stays behind.</summary>
public sealed record PayloadRecurring(string Category, string Cadence, decimal MonthlyCost, bool IsSubscription);

/// <summary>
/// What the privacy preview shows the user. Holds the user's own data (the examples), so it is only ever
/// returned to that user and is never part of <see cref="AiPayload"/>. <c>Passed</c> is true only when every
/// check found nothing; an AI call must not be made otherwise.
/// </summary>
public sealed record PayloadPrivacyReport(
    bool Passed,
    IReadOnlyList<PrivacyExample> Examples,
    IReadOnlyList<WithheldField> Withheld,
    IReadOnlyList<PrivacyCheck> Checks);

/// <summary>One of the user's real rows, and the category bucket it reaches the payload as.</summary>
public sealed record PrivacyExample(
    DateOnly Date,
    string Description,
    string AccountName,
    string? Mask,
    decimal Amount,
    string SentAs);

/// <summary>How many values of one identifying field were left out of the payload.</summary>
public sealed record WithheldField(string Field, int Count);

/// <summary>One validator rule and how many string values in the payload it matched.</summary>
public sealed record PrivacyCheck(string Id, string Label, int Hits, bool Passed);

/// <summary>The payload, and the report about it, built together from one read of the ledger.</summary>
public sealed record AiPayloadResult(AiPayload Payload, PayloadPrivacyReport Report);

/// <summary>
/// The privacy preview. <c>PayloadJson</c> is the exact serialised payload, so the "view exact JSON" toggle
/// shows the bytes that would be sent rather than the browser's own re-serialisation of them.
/// </summary>
public sealed record AiPayloadPreviewResponse(AiPayload Payload, string PayloadJson, PayloadPrivacyReport Report);
