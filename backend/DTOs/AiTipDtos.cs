namespace backend.DTOs;

/// <summary>
/// Pressing "Generate tips" is the consent to send the previewed payload. The flag makes that explicit, so a
/// stray request with no body never sends anything.
/// </summary>
public sealed record GenerateTipsRequest(bool Consent);

/// <summary>
/// One tip. <c>Id</c> is the rule that produced it, so AI wording and template wording carry the same id.
/// <c>Source</c> is "ai" or "template" for this tip alone: one rejected tip falls back without discarding the rest.
/// </summary>
public sealed record AiTip(string Id, string Title, string Text, string Source, IReadOnlyList<RoadmapStep> Steps);

/// <summary>
/// One step of a tip's roadmap, computed by the rules and never sent to or written by the model. <c>Per</c> is
/// "month" for a monthly amount or "total" for a balance to reach; <c>By</c> is the estimated month, assuming
/// today's average surplus holds.
/// </summary>
public sealed record RoadmapStep(string Label, decimal Amount, string Per, DateOnly? By);

/// <summary>
/// <c>Source</c> is "ai" when every tip is the model's checked wording, "template" when none is (the model was
/// off, failed, or nothing it wrote passed), and "mixed" otherwise. <c>Model</c> is null only for "template".
/// </summary>
public sealed record AiTipsResponse(
    string Source,
    string? Model,
    IReadOnlyList<AiTip> Tips,
    IReadOnlyList<string> DataGaps,
    DateTime CreatedAt);
