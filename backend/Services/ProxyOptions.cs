namespace backend.Services;

/// <summary>
/// Which peers may set X-Forwarded-For / X-Forwarded-Proto. A header from anyone else is ignored,
/// so a client cannot pick its own rate-limit bucket.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// CIDR ranges of the reverse proxies in front of the API. The default covers loopback and
    /// private ranges (Funnel on the same host, a Docker bridge); set the exact range in production.
    /// </summary>
    public string[] KnownNetworks { get; set; } =
    [
        "127.0.0.0/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "fc00::/7",
    ];
}
