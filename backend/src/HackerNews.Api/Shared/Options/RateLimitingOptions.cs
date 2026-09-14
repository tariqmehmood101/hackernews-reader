namespace HackerNews.Api.Shared.Options;

/// <summary>
/// Request throttling. Bound through the options system rather than read straight off
/// <c>builder.Configuration</c>, so the value is resolved from the final configuration at request
/// time — which is what makes it reachable from tests and from a host that reloads settings.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests allowed per client, per fixed one-minute window.</summary>
    public int PermitLimitPerMinute { get; set; } = 100;
}
