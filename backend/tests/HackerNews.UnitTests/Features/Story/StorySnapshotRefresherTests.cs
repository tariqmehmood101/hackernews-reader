using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HackerNews.UnitTests.Features.Story;

/// <summary>
/// Exercises a refresh pass directly. Splitting the logic out of the hosted service is what makes
/// these tests possible without a host, a timer or a sleep.
/// </summary>
public sealed class StorySnapshotRefresherTests
{
    private readonly IHackerNewsClient _client = Substitute.For<IHackerNewsClient>();
    private readonly StorySnapshotCache _cache = new(Given.CacheOptions());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StorySnapshotRefresher CreateRefresher(
        int fullRefreshEveryNCycles = 0, int minimumYieldPercent = 50) =>
        new(
            _client,
            _cache,
            Given.CacheOptions(
                fullRefreshEveryNCycles: fullRefreshEveryNCycles,
                minimumYieldPercent: minimumYieldPercent),
            NullLogger<StorySnapshotRefresher>.Instance);

    private void GivenIds(params int[] ids) =>
        _client.GetNewestStoryIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<int>>(ids));

    private void GivenStoriesResolve() =>
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<StoryDto?>(Given.Story(call.Arg<int>(), minutesOld: call.Arg<int>())));

    [Fact]
    public async Task Publishes_a_snapshot_of_everything_it_resolved()
    {
        GivenIds(1, 2, 3);
        GivenStoriesResolve();

        await CreateRefresher().RefreshAsync(Ct);

        _cache.Current.IsLoaded.ShouldBeTrue();
        _cache.Current.Stories.Select(s => s.Id).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Orders_the_snapshot_newest_first()
    {
        GivenIds(1, 2, 3);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<StoryDto?>(
                // id 2 is the newest, id 3 the oldest, so id order != time order.
                Given.Story(call.Arg<int>(), minutesOld: call.Arg<int>() switch { 2 => 0, 1 => 5, _ => 10 })));

        await CreateRefresher().RefreshAsync(Ct);

        _cache.Current.Stories.Select(s => s.Id).ShouldBe([2, 1, 3]);
    }

    [Fact]
    public async Task Skips_ids_that_resolve_to_nothing()
    {
        GivenIds(1, 2, 3);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<int>() == 2 ? null : Given.Story(call.Arg<int>())));

        await CreateRefresher().RefreshAsync(Ct);

        _cache.Current.Stories.Select(s => s.Id).ShouldBe([1, 3]);
    }

    [Fact]
    public async Task A_second_pass_fetches_only_the_ids_it_has_not_seen()
    {
        GivenIds(1, 2, 3);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);
        _client.ClearReceivedCalls();

        GivenIds(1, 2, 3, 4);
        await refresher.RefreshAsync(Ct);

        // This is what keeps steady-state traffic to a few dozen calls instead of 500.
        await _client.Received(1).GetStoryAsync(4, Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetStoryAsync(1, Arg.Any<CancellationToken>());
        _cache.Current.Stories.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Ids_that_drop_out_of_the_feed_are_evicted_from_the_item_cache()
    {
        GivenIds(1, 2, 3);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);

        GivenIds(2, 3);
        await refresher.RefreshAsync(Ct);
        _cache.Current.Stories.Select(s => s.Id).ShouldBe([2, 3]);

        // If id 1 had been retained, it would not be re-fetched when it reappears.
        _client.ClearReceivedCalls();
        GivenIds(1, 2, 3);
        await refresher.RefreshAsync(Ct);

        await _client.Received(1).GetStoryAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_full_refresh_cycle_re_fetches_everything_so_scores_stop_drifting()
    {
        GivenIds(1, 2);
        GivenStoriesResolve();
        var refresher = CreateRefresher(fullRefreshEveryNCycles: 2);

        await refresher.RefreshAsync(Ct);
        _client.ClearReceivedCalls();
        await refresher.RefreshAsync(Ct);

        await _client.Received(1).GetStoryAsync(1, Arg.Any<CancellationToken>());
        await _client.Received(1).GetStoryAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_id_list_is_treated_as_a_blip_and_leaves_the_snapshot_alone()
    {
        GivenIds(1, 2);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);
        var good = _cache.Current;

        GivenIds();
        await refresher.RefreshAsync(Ct);

        _cache.Current.ShouldBeSameAs(good);
    }

    [Fact]
    public async Task One_failing_item_does_not_abort_the_whole_pass()
    {
        GivenIds(1, 2, 3);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<int>() == 2
                ? throw new HttpRequestException("upstream hiccup")
                : Task.FromResult<StoryDto?>(Given.Story(call.Arg<int>())));

        await CreateRefresher().RefreshAsync(Ct);

        _cache.Current.Stories.Select(s => s.Id).ShouldBe([1, 3]);
    }

    [Fact]
    public async Task When_every_item_fails_the_previous_snapshot_survives()
    {
        GivenIds(1, 2);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);
        var good = _cache.Current;

        // New ids so nothing can be served from the item cache, and all of them fail.
        GivenIds(90, 91);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("upstream down"));

        await refresher.RefreshAsync(Ct);

        _cache.Current.ShouldBeSameAs(good);
    }

    [Fact]
    public async Task A_full_refresh_that_mostly_fails_keeps_the_stories_it_could_not_re_fetch()
    {
        // The regression this guards: a full cycle used to discard the item cache before knowing
        // the re-fetch would work, so a degraded upstream collapsed a whole feed into whatever
        // few items happened to come back.
        GivenIds([.. Enumerable.Range(1, 10)]);
        GivenStoriesResolve();
        var refresher = CreateRefresher(fullRefreshEveryNCycles: 2);
        await refresher.RefreshAsync(Ct);
        _cache.Current.Stories.Count.ShouldBe(10);

        // The second pass is the full one, and upstream now fails for all but one story.
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<int>() == 1
                ? Task.FromResult<StoryDto?>(Given.Story(1, "Re-fetched"))
                : throw new HttpRequestException("upstream hiccup"));

        await refresher.RefreshAsync(Ct);

        // All ten survive: the one that re-fetched is updated, the nine that failed stay as they
        // were — stale scores, which is the whole point of preferring them to nothing.
        _cache.Current.Stories.Count.ShouldBe(10);
        _cache.Current.Stories.ShouldContain(story => story.Title == "Re-fetched");
    }

    [Fact]
    public async Task A_full_refresh_still_retires_a_story_that_has_since_been_deleted()
    {
        GivenIds(1, 2, 3, 4);
        GivenStoriesResolve();
        var refresher = CreateRefresher(fullRefreshEveryNCycles: 2);
        await refresher.RefreshAsync(Ct);

        // Story 4 has been deleted or flagged since we cached it. A null is upstream being
        // definitive, unlike a throw, so the cached copy must go rather than linger.
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                call.Arg<int>() == 4 ? null : Given.Story(call.Arg<int>())));

        await refresher.RefreshAsync(Ct);

        _cache.Current.Stories.Select(s => s.Id).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task A_pass_that_resolves_far_too_little_keeps_the_previous_snapshot()
    {
        GivenIds([.. Enumerable.Range(1, 10)]);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);
        var good = _cache.Current;

        // An entirely new id list, of which only two resolve. 20% is not a feed worth publishing
        // over a healthy one.
        GivenIds([.. Enumerable.Range(100, 10)]);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<int>() < 102
                ? Task.FromResult<StoryDto?>(Given.Story(call.Arg<int>()))
                : throw new HttpRequestException("upstream hiccup"));

        await refresher.RefreshAsync(Ct);

        _cache.Current.ShouldBeSameAs(good);
    }

    [Fact]
    public async Task A_cold_cache_publishes_a_partial_result_rather_than_answering_503_forever()
    {
        GivenIds([.. Enumerable.Range(1, 10)]);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<int>() == 1
                ? Task.FromResult<StoryDto?>(Given.Story(1))
                : throw new HttpRequestException("upstream hiccup"));

        await CreateRefresher().RefreshAsync(Ct);

        // A 10% yield would be refused against a loaded snapshot, but there is nothing to protect
        // here and a partial feed beats 503. The gaps fill in next pass.
        _cache.Current.IsLoaded.ShouldBeTrue();
        _cache.Current.Stories.Select(s => s.Id).ShouldBe([1]);
    }

    [Fact]
    public async Task Turning_the_yield_floor_off_still_refuses_to_publish_nothing_at_all()
    {
        GivenIds(1, 2);
        GivenStoriesResolve();
        var refresher = CreateRefresher(minimumYieldPercent: 0);
        await refresher.RefreshAsync(Ct);
        var good = _cache.Current;

        GivenIds(90, 91);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("upstream down"));

        await refresher.RefreshAsync(Ct);

        _cache.Current.ShouldBeSameAs(good);
    }

    [Fact]
    public async Task Turning_the_yield_floor_off_publishes_whatever_a_pass_resolved()
    {
        GivenIds([.. Enumerable.Range(1, 10)]);
        GivenStoriesResolve();
        var refresher = CreateRefresher(minimumYieldPercent: 0);
        await refresher.RefreshAsync(Ct);

        GivenIds([.. Enumerable.Range(100, 10)]);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<int>() == 100
                ? Task.FromResult<StoryDto?>(Given.Story(100))
                : throw new HttpRequestException("upstream hiccup"));

        await refresher.RefreshAsync(Ct);

        _cache.Current.Stories.Select(s => s.Id).ShouldBe([100]);
    }

    [Fact]
    public async Task A_failure_fetching_the_id_list_propagates_and_leaves_the_snapshot_alone()
    {
        GivenIds(1);
        GivenStoriesResolve();
        var refresher = CreateRefresher();
        await refresher.RefreshAsync(Ct);
        var good = _cache.Current;

        _client.GetNewestStoryIdsAsync(Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("upstream down"));

        // The hosted service catches this, logs it and retries on the shorter interval.
        await Should.ThrowAsync<HttpRequestException>(() => refresher.RefreshAsync(Ct));
        _cache.Current.ShouldBeSameAs(good);
    }

    [Fact]
    public async Task Never_exceeds_the_configured_fetch_concurrency()
    {
        var inFlight = 0;
        var peak = 0;
        GivenIds([.. Enumerable.Range(1, 40)]);
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async Task<StoryDto?> (call) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, current);
                await Task.Yield();
                Interlocked.Decrement(ref inFlight);
                return Given.Story(call.Arg<int>());
            });

        // Given.CacheOptions defaults MaxConcurrentItemFetches to 4.
        await CreateRefresher().RefreshAsync(Ct);

        peak.ShouldBeLessThanOrEqualTo(4);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }
}
