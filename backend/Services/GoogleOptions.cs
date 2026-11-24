namespace backend.Services;

/// <summary>
/// Google Sign-In settings. The client ID is public (the browser needs it too) but lives in config
/// so production can use a different Google client from development.
/// </summary>
public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>The OAuth Web client ID. Every ID token must be issued for exactly this audience.</summary>
    public string ClientId { get; set; } = "";
}
