using System.Collections.Concurrent;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Options;
using HackerNews.Api.Features.Story.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Features.Story.Services;

/// <inheritdoc cref="IStorySnapshotRefresher"/>
public sealed class StorySnapshotRefresher(
    IHackerNewsClient client,
    IStorySnapshotCache cache,
    IOptions<StoryCacheOptions> options,
    ILogger<StorySnapshotRefresher> logger) : IStorySnapshotRefresher
{
    /// <summary>Already-downloaded story detail, keyed by id. See the note on the class.</summary>
    private readonly ConcurrentDictionary<int, StoryDto> _fetchCache = new();

    private readonly StoryCacheOptions _options = options.Value;
    private int _passCount;

    /// <summary>One pass, in five steps.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // 1. Ask upstream which stories are currently newest.
        var liveIds = await client.GetNewestStoryIdsAsync(cancellationToken);
        if (liveIds.Count == 0)
        {
            // An empty id list is far more likely to be an upstream blip than a genuinely empty
            // feed, so keep whatever snapshot we already have.
            logger.LogWarning("Upstream returned no story ids; keeping the previous snapshot.");
            return;
        }

        var published = cache.Current;
        var pass = BeginPass();

        // 2. Forget anything that has fallen out of that window.
        EvictStoriesNoLongerListed(liveIds);

        // 3. Download what we are missing — normally just the new arrivals.
        await DownloadAsync(SelectIdsToDownload(liveIds, pass), cancellationToken);

        // 4. Rebuild the feed from the live ids and whatever detail we now hold.
        var stories = BuildFeed(liveIds);

        // 5. Publish, unless doing so would replace a good snapshot with a gutted one.
        if (!IsWorthPublishing(stories.Count, liveIds.Count, published))
        {
            // The ids that failed carry no cache entry, so the next pass retries exactly those.
            logger.LogError(
                "Resolved only {StoryCount} of {IdCount} story ids; keeping the previous snapshot "
                + "of {PreviousCount}.",
                stories.Count, liveIds.Count, published.Stories.Count);
            return;
        }

        cache.Replace(new StorySnapshot(stories, DateTimeOffset.UtcNow));
        logger.LogInformation(
            "Snapshot refreshed: {StoryCount} stories from {IdCount} ids.",
            stories.Count, liveIds.Count);
    }

    /// <summary>What this pass is: its number, and whether it re-downloads everything.</summary>
    private readonly record struct Pass(int Number, bool IsFullRefresh);

    /// <summary>
    /// Counts the pass and decides whether it is a full refresh. Counted here rather than inside
    /// the id selection so the state change is visible in <see cref="RefreshAsync"/>, and only
    /// after the empty-id guard — a pass that found nothing should not advance the cadence.
    /// </summary>
    private Pass BeginPass()
    {
        var number = Interlocked.Increment(ref _passCount);
        var everyN = _options.FullRefreshEveryNCycles;

        return new Pass(number, IsFullRefresh: everyN > 0 && number % everyN == 0);
    }

    /// <summary>
    /// Drops cached detail for stories upstream no longer lists. This is what keeps the cache
    /// bounded to the newest window instead of growing without limit.
    /// </summary>
    private void EvictStoriesNoLongerListed(IReadOnlyList<int> liveIds)
    {
        var live = liveIds.ToHashSet();

        foreach (var goneId in _fetchCache.Keys.Where(id => !live.Contains(id)))
        {
            _fetchCache.TryRemove(goneId, out _);
        }
    }

    /// <summary>
    /// Normally just the ids never seen before. On a full-refresh pass, every id &mdash; because
    /// reusing cached detail means <c>score</c> and <c>descendants</c> drift, and a periodic
    /// re-download corrects them.
    /// </summary>
    /// <remarks>
    /// A full refresh downloads <em>over the top of</em> the cache rather than clearing it first.
    /// That distinction matters: if it cleared first and upstream were degraded, most re-downloads
    /// would fail and the feed would collapse to a handful of stories. Overwriting means a story
    /// whose re-download fails simply stays as it was — stale, but present.
    /// </remarks>
    private IReadOnlyList<int> SelectIdsToDownload(IReadOnlyList<int> liveIds, Pass pass)
    {
        if (!pass.IsFullRefresh)
        {
            return liveIds.Where(id => !_fetchCache.ContainsKey(id)).ToList();
        }

        logger.LogInformation(
            "Pass {PassNumber}: re-downloading every story so scores stop drifting.", pass.Number);

        return liveIds;
    }

    /// <summary>
    /// Downloads the given ids, a bounded number at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bound is not politeness towards a free API; it is self-preservation, and it would still
    /// be here against an API we paid for.
    /// </para>
    /// <para>
    /// A pass can list 500 ids. Unbounded, that is 500 sockets at once, because nothing caps the
    /// typed client's connections per server &mdash; and App Service allots each instance a modest
    /// number of outbound ports, shared by everything the process does. Worse, the resilience
    /// pipeline retries, so a bad moment upstream turns 500 requests into a few thousand, and the
    /// circuit breaker it shares with the single-story lookup trips on our own burst &mdash; making
    /// <c>GET /api/stories/{id}</c> answer 503 because the refresher stampeded.
    /// </para>
    /// </remarks>
    private async Task DownloadAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        logger.LogDebug("Downloading {DownloadCount} stories.", ids.Count);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrentItemFetches,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(ids, options, DownloadOneAsync);
    }

    /// <summary>
    /// Downloads one story, distinguishing upstream's two kinds of "no story here".
    /// </summary>
    /// <remarks>
    /// A <c>null</c> body is <em>definitive</em>: the story was deleted, flagged dead, or purged,
    /// so any copy we hold is retired. An exception is <em>transient</em>: the network or upstream
    /// faltered, so we keep what we have and try again next pass. Collapsing the two would either
    /// resurrect deleted stories or delete live ones on a bad network moment.
    /// </remarks>
    private async ValueTask DownloadOneAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            if (await client.GetStoryAsync(id, cancellationToken) is { } story)
            {
                _fetchCache[id] = story;
            }
            else
            {
                _fetchCache.TryRemove(id, out _);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad item must never abort the pass.
            logger.LogWarning(ex, "Failed to fetch story {StoryId}; keeping any cached copy.", id);
        }
    }

    /// <summary>
    /// The feed readers will see: the live ids, in upstream's order of newness, skipping any whose
    /// detail we could not obtain.
    /// </summary>
    private List<StoryDto> BuildFeed(IReadOnlyList<int> liveIds) =>
        liveIds
            .Select(id => _fetchCache.GetValueOrDefault(id))
            .OfType<StoryDto>()
            .OrderByDescending(story => story.Time)
            .ToList();

    /// <summary>
    /// Whether a pass resolved enough to be allowed to replace what is already published. A cold
    /// cache publishes anything it managed to resolve &mdash; a partial feed beats answering 503,
    /// and the gaps fill in on the next pass.
    /// </summary>
    private bool IsWorthPublishing(int resolved, int listed, StorySnapshot published)
    {
        if (!published.IsLoaded)
        {
            return resolved > 0;
        }

        var required = (int)Math.Ceiling(listed * (_options.MinimumYieldPercent / 100.0));

        return resolved >= Math.Max(1, required);
    }
}
