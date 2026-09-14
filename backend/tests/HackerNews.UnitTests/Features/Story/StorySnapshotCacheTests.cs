using HackerNews.Api.Exceptions;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services;
using HackerNews.UnitTests.TestSupport;

namespace HackerNews.UnitTests.Features.Story;

public sealed class StorySnapshotCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Starts_empty_and_not_loaded()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions());

        cache.Current.ShouldBeSameAs(StorySnapshot.Empty);
        cache.Current.IsLoaded.ShouldBeFalse();
        cache.Current.Stories.ShouldBeEmpty();
    }

    [Fact]
    public void Replace_swaps_the_snapshot_wholesale()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions());
        var snapshot = Given.Snapshot(Given.Stories(3));

        cache.Replace(snapshot);

        cache.Current.ShouldBeSameAs(snapshot);
        cache.Current.IsLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_returns_at_once_once_a_snapshot_exists()
    {
        // A long timeout would hang this test if the fast path were not taken.
        var cache = new StorySnapshotCache(Given.CacheOptions(initialLoadTimeoutSeconds: 600));
        var snapshot = Given.Snapshot(Given.Stories(1));
        cache.Replace(snapshot);

        (await cache.EnsureLoadedAsync(Ct)).ShouldBeSameAs(snapshot);
    }

    [Fact]
    public async Task EnsureLoadedAsync_reports_the_upstream_as_unavailable_when_the_first_load_never_arrives()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions(initialLoadTimeoutSeconds: 0));

        // Deliberately not an empty 200: a client cannot tell that apart from "no stories exist".
        await Should.ThrowAsync<UpstreamUnavailableException>(() => cache.EnsureLoadedAsync(Ct));
    }

    [Fact]
    public async Task EnsureLoadedAsync_unblocks_as_soon_as_the_first_refresh_lands()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions(initialLoadTimeoutSeconds: 30));
        var snapshot = Given.Snapshot(Given.Stories(2));

        var waiting = cache.EnsureLoadedAsync(Ct);
        cache.Replace(snapshot);

        (await waiting).ShouldBeSameAs(snapshot);
    }

    [Fact]
    public async Task EnsureLoadedAsync_honours_cancellation()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions(initialLoadTimeoutSeconds: 30));
        using var cts = new CancellationTokenSource();

        var waiting = cache.EnsureLoadedAsync(cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void Replace_rejects_null()
    {
        var cache = new StorySnapshotCache(Given.CacheOptions());

        Should.Throw<ArgumentNullException>(() => cache.Replace(null!));
    }
}
