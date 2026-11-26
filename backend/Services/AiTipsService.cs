using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public enum TipsOutcome
{
    Generated,
    PrivacyBlocked,
    DailyLimitReached,
}

public sealed record TipsResult(TipsOutcome Outcome, AiTipsResponse? Response = null);

/// <summary>
/// Builds the payload, refuses if the privacy check did not pass, asks the model to reword the rule-chosen tips,
/// validates the reply, and falls back to template text whenever the model is off, fails or is wrong. Every
/// generation is stored, which is both the consent record and the daily-limit count.
/// </summary>
public sealed class AiTipsService(
    AppDbContext db,
    AiPayloadBuilder payloadBuilder,
    IAdvisorModel model,
    IOptions<AiAdvisorOptions> options,
    ILogger<AiTipsService> logger)
{
    public async Task<TipsResult> GenerateAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var dayStart = now.Date;
        var usedToday = await db.AiRecommendations
            .CountAsync(row => row.UserId == userId && row.CreatedAt >= dayStart, cancellationToken);
        if (usedToday >= options.Value.DailyLimitPerUser)
        {
            return new TipsResult(TipsOutcome.DailyLimitReached);
        }

        var built = await payloadBuilder.BuildAsync(userId, DateOnly.FromDateTime(now), cancellationToken);
        if (!built.Report.Passed)
        {
            // Fail closed: nothing is sent and nothing is recorded as consented.
            return new TipsResult(TipsOutcome.PrivacyBlocked);
        }

        var payloadJson = PayloadPrivacyValidator.Serialize(built.Payload);
        var candidates = AiTips.Candidates(built.Payload);
        var (tips, modelName) = await AskModelAsync(payloadJson, candidates, cancellationToken);
        var aiCount = tips.Count(tip => tip.Source == AiTips.SourceAi);
        var source = aiCount == 0 ? TipSource.Template : aiCount == tips.Count ? TipSource.Ai : TipSource.Mixed;
        if (source == TipSource.Template)
        {
            modelName = null;
        }

        var row = new AiRecommendation
        {
            UserId = userId,
            PayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))),
            PromptVersion = AiTips.PromptVersion,
            Source = source,
            Model = modelName,
            TipsJson = JsonSerializer.Serialize(tips, JsonSerializerOptions.Web),
            CreatedAt = now,
        };
        db.AiRecommendations.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return new TipsResult(TipsOutcome.Generated, new AiTipsResponse(
            source switch
            {
                TipSource.Ai => AiTips.SourceAi,
                TipSource.Mixed => AiTips.SourceMixed,
                _ => AiTips.SourceTemplate,
            },
            modelName,
            tips,
            AiTips.DataGaps(built.Payload),
            row.CreatedAt));
    }

    /// <summary>
    /// The model's checked tips topped up with templates, and the model's name; or the templates and null when
    /// the model is off or failed.
    /// </summary>
    private async Task<(IReadOnlyList<AiTip> Tips, string? Model)> AskModelAsync(
        string payloadJson,
        IReadOnlyList<TipCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var templates = AiTips.Templates(candidates);
        if (candidates.Count == 0 || !model.IsConfigured)
        {
            return (templates, null);
        }

        // Only the payload (already checked) and our own wording built from it leave the API.
        var userContent = new JsonObject
        {
            ["payload"] = JsonNode.Parse(payloadJson),
            ["candidates"] = new JsonArray(candidates
                .Select(candidate => (JsonNode)new JsonObject
                {
                    ["id"] = candidate.Id,
                    ["title"] = candidate.Title,
                    ["text"] = candidate.Text,
                })
                .ToArray()),
        }.ToJsonString();

        try
        {
            var reply = await model.CompleteAsync(AiTips.SystemPrompt, userContent, cancellationToken);
            var validated = AiTips.Validate(reply.Content, candidates);
            var combined = AiTips.Combine(validated, candidates);
            if (validated.Count < combined.Count)
            {
                logger.LogWarning(
                    "AI tips reply from {Model}: {Rejected} of {Shown} tips use template wording.",
                    reply.Model, combined.Count - validated.Count, combined.Count);
            }

            return (combined, reply.Model);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is AdvisorModelException or HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException here is the HttpClient timeout, not the caller leaving.
            logger.LogWarning("AI tips call failed ({Reason}); showing templates.", exception.GetType().Name);
        }

        return (templates, null);
    }
}
