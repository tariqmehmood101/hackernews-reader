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
    /// <summary>
    /// Item detail keyed by story id, reused between passes so a steady-state refresh fetches only
    /// the handful of stories that are actually new rather than all 500.
    /// </summary>
    private readonly ConcurrentDictionary<int, StoryDto> _known = new();

    private readonly StoryCacheOptions _options = options.Value;
    private int _cycle;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var ids = await client.GetNewestStoryIdsAsync(cancellationToken);
        if (ids.Count == 0)
        {
            // An empty id list is far more likely to be an upstream blip than a genuinely empty
            // feed, so keep whatever snapshot we already have.
            logger.LogWarning("Upstream returned no story ids; keeping the previous snapshot.");
            return;
        }

        var previous = cache.Current;

        var live = ids.ToHashSet();
        foreach (var stale in _known.Keys.Where(id => !live.Contains(id)))
        {
            _known.TryRemove(stale, out _);
        }

        await FetchAsync(SelectIdsToFetch(ids), cancellationToken);

        var stories = ids
            .Select(id => _known.GetValueOrDefault(id))
            .OfType<StoryDto>()
            .OrderByDescending(story => story.Time)
            .ToList();

        if (!IsWorthPublishing(stories.Count, ids.Count, previous))
        {
            // Publishing now would trade a good snapshot for a collapsed one. Keep what we have;
            // the ids that failed carry no cache entry, so the next pass retries exactly those.
            logger.LogError(
                "Resolved only {StoryCount} of {IdCount} story ids; keeping the previous snapshot "
                + "of {PreviousCount}.",
                stories.Count, ids.Count, previous.Stories.Count);
            return;
        }

        cache.Replace(new StorySnapshot(stories, DateTimeOffset.UtcNow));
        logger.LogInformation(
            "Snapshot refreshed: {StoryCount} stories from {IdCount} ids.", stories.Count, ids.Count);
    }

    /// <summary>
    /// Which ids this pass actually fetches: normally only the ones never seen before, but on a
    /// full cycle every id, re-fetched <em>over the top of</em> the cached copy. Re-fetching over
    /// the top rather than clearing first is what keeps a half-failed full refresh from shrinking
    /// the feed &mdash; a story whose re-fetch fails stays put, merely stale, instead of vanishing.
    /// </summary>
    private IReadOnlyList<int> SelectIdsToFetch(IReadOnlyList<int> ids)
    {
        var cycle = Interlocked.Increment(ref _cycle);
        if (_options.FullRefreshEveryNCycles > 0 && cycle % _options.FullRefreshEveryNCycles == 0)
        {
            logger.LogInformation(
                "Cycle {Cycle}: re-fetching every story so scores stop drifting.", cycle);
            return ids;
        }

        return ids.Where(id => !_known.ContainsKey(id)).ToList();
    }

    private async Task FetchAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        logger.LogDebug("Fetching {FetchCount} stories.", ids.Count);

        using var gate = new SemaphoreSlim(_options.MaxConcurrentItemFetches);
        await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (await client.GetStoryAsync(id, cancellationToken) is { } story)
                {
                    _known[id] = story;
                }
                else
                {
                    // A definitive "gone" — deleted, dead, or purged upstream. Drop any copy we
                    // hold, which is how a full cycle retires stories that died since we cached
                    // them. On a first fetch there is nothing to drop and this is a no-op.
                    _known.TryRemove(id, out _);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Transient, unlike a null: one bad item must not abort the pass, and whatever we
                // already had is kept rather than dropped. It is retried next cycle.
                logger.LogWarning(ex, "Failed to fetch story {StoryId}; keeping any cached copy.", id);
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    /// <summary>
    /// Whether a pass resolved enough to be allowed to replace what is already published. A cold
    /// cache publishes anything it managed to resolve &mdash; a partial feed beats answering 503,
    /// and the gaps fill in on the next pass.
    /// </summary>
    private bool IsWorthPublishing(int resolved, int listed, StorySnapshot previous)
    {
        if (!previous.IsLoaded)
        {
            return resolved > 0;
        }

        var required = (int)Math.Ceiling(listed * (_options.MinimumYieldPercent / 100.0));
        return resolved >= Math.Max(1, required);
    }
}
