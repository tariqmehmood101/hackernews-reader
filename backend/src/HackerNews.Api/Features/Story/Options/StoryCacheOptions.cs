namespace HackerNews.Api.Features.Story.Options;

public sealed class StoryCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>How long to wait between refresh passes.</summary>
    public int RefreshIntervalSeconds { get; set; } = 300;

    /// <summary>Shorter delay used after a failed pass, so we recover faster than the normal cadence.</summary>
    public int RetryIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// How long a request will wait for the very first snapshot before the API answers 503.
    /// </summary>
    public int InitialLoadTimeoutSeconds { get; set; } = 10;

    /// <summary>Concurrent upstream item fetches. Keeps us a polite client of a free API.</summary>
    public int MaxConcurrentItemFetches { get; set; } = 15;

    /// <summary>
    /// Cached items are reused across passes, so <c>score</c> and <c>descendants</c> drift. Every
    /// Nth pass re-fetches every story rather than only the new ones. Default 12 (~hourly at 300s).
    /// </summary>
    public int FullRefreshEveryNCycles { get; set; } = 12;

    /// <summary>
    /// The share of the ids upstream listed that a pass must resolve before it may replace an
    /// already-loaded snapshot. Stops a badly degraded upstream from collapsing a full feed into a
    /// handful of stories. 0 publishes whatever a pass resolves, short of nothing at all; a cold
    /// cache always publishes what it has, since a partial feed beats answering 503.
    /// </summary>
    public int MinimumYieldPercent { get; set; } = 50;

    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(RefreshIntervalSeconds);
    public TimeSpan RetryInterval => TimeSpan.FromSeconds(RetryIntervalSeconds);
    public TimeSpan InitialLoadTimeout => TimeSpan.FromSeconds(InitialLoadTimeoutSeconds);
}
