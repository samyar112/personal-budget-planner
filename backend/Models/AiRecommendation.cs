namespace backend.Models;

public enum TipSource
{
    Ai,
    Template,

    /// <summary>Some tips are the model's checked wording, the rest template text.</summary>
    Mixed,
}

/// <summary>
/// One "Generate tips" press: the user's consent to send the payload, and the record of what came back. The
/// payload itself is not stored; its hash, the prompt version and the model are enough to trace any tip to the
/// input and the wording that produced it. Rows also count toward the per-user daily limit.
/// </summary>
public class AiRecommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Hex SHA-256 of the exact serialised payload that was (or would have been) sent.</summary>
    public required string PayloadSha256 { get; set; }

    public required string PromptVersion { get; set; }

    public TipSource Source { get; set; }

    /// <summary>The model that wrote the shown AI tips; null when every tip is template text.</summary>
    public string? Model { get; set; }

    /// <summary>The tips as shown. Derived from payload numbers and our own wording, so no identifying text.</summary>
    public required string TipsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
