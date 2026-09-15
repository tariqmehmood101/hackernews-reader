using HackerNews.Api.Features.Story;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.UnitTests.TestSupport;
using NSubstitute;

namespace HackerNews.UnitTests.Features.Story;

public sealed class GetNewestStoriesHandlerTests
{
    private readonly IStorySnapshotCache _cache = Substitute.For<IStorySnapshotCache>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GetNewestStoriesHandler CreateHandler(IReadOnlyList<StoryDto> stories)
    {
        _cache.EnsureLoadedAsync(Arg.Any<CancellationToken>()).Returns(Given.Snapshot(stories));
        return new GetNewestStoriesHandler(_cache);
    }

    [Fact]
    public async Task Returns_the_requested_page_size()
    {
        var handler = CreateHandler(Given.Stories(50));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 1, PageSize = 20 }, Ct);

        response.Result.Items.Count.ShouldBe(20);
        response.Result.TotalCount.ShouldBe(50);
        response.Result.TotalPages.ShouldBe(3);
        response.Result.HasPreviousPage.ShouldBeFalse();
        response.Result.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task Skips_to_the_requested_page()
    {
        var handler = CreateHandler(Given.Stories(50));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 3, PageSize = 20 }, Ct);

        response.Result.Items.Count.ShouldBe(10);
        response.Result.Items[0].Id.ShouldBe(41);
        response.Result.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_still_reports_the_real_total()
    {
        var handler = CreateHandler(Given.Stories(50));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 99, PageSize = 20 }, Ct);

        response.Result.Items.ShouldBeEmpty();
        response.Result.TotalCount.ShouldBe(50);
        response.Result.TotalPages.ShouldBe(3);
    }

    [Fact]
    public async Task An_exact_multiple_does_not_produce_a_trailing_empty_page()
    {
        var handler = CreateHandler(Given.Stories(40));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 2, PageSize = 20 }, Ct);

        response.Result.TotalPages.ShouldBe(2);
        response.Result.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_size_larger_than_the_whole_feed_yields_a_single_full_page()
    {
        var handler = CreateHandler(Given.Stories(45));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 1, PageSize = 100 }, Ct);

        response.Result.Items.Count.ShouldBe(45);
        response.Result.TotalPages.ShouldBe(1);
        response.Result.HasNextPage.ShouldBeFalse();
        response.Result.HasPreviousPage.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_size_of_one_makes_every_story_its_own_page()
    {
        var handler = CreateHandler(Given.Stories(7));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 4, PageSize = 1 }, Ct);

        response.Result.Items.ShouldHaveSingleItem().Id.ShouldBe(4);
        response.Result.TotalPages.ShouldBe(7);
        response.Result.HasPreviousPage.ShouldBeTrue();
        response.Result.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task The_final_page_carries_the_remainder_and_reports_no_next_page()
    {
        // 45 across pages of 20 leaves 5 on page 3 — the case an off-by-one would truncate.
        var handler = CreateHandler(Given.Stories(45));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Page = 3, PageSize = 20 }, Ct);

        response.Result.Items.Count.ShouldBe(5);
        response.Result.Items[0].Id.ShouldBe(41);
        response.Result.HasNextPage.ShouldBeFalse();
        response.Result.HasPreviousPage.ShouldBeTrue();
    }

    [Fact]
    public async Task A_story_with_no_author_is_still_returned()
    {
        // `by` is optional upstream, and the contract allows null rather than dropping the story.
        var handler = CreateHandler([Given.Story(1, "Anonymous submission", by: null)]);

        var response = await handler.Handle(new GetNewestStoriesHandler.Query(), Ct);

        var story = response.Result.Items.ShouldHaveSingleItem();
        story.By.ShouldBeNull();
        story.Title.ShouldBe("Anonymous submission");
    }

    [Fact]
    public async Task Searching_matches_the_title_even_when_the_author_is_missing()
    {
        // The author filter must tolerate a null rather than throwing while matching.
        var handler = CreateHandler([
            Given.Story(1, "Rust without an author", by: null),
            Given.Story(2, "Go with one", by: "someone")
        ]);

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = "rust" }, Ct);

        response.Result.Items.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Theory]
    [InlineData("RUST")]
    [InlineData("rust")]
    [InlineData("rUsT")]
    public async Task Search_matches_the_title_regardless_of_case(string term)
    {
        var handler = CreateHandler([
            Given.Story(1, "Learning Rust in 2026"),
            Given.Story(2, "Go concurrency patterns")
        ]);

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = term }, Ct);

        response.Result.Items.ShouldHaveSingleItem().Id.ShouldBe(1);
        response.Result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_also_matches_the_author()
    {
        var handler = CreateHandler([
            Given.Story(1, "Something", by: "pg"),
            Given.Story(2, "Other", by: "dhh")
        ]);

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = "dhh" }, Ct);

        response.Result.Items.ShouldHaveSingleItem().Id.ShouldBe(2);
    }

    [Fact]
    public async Task Search_matches_a_substring()
    {
        var handler = CreateHandler([Given.Story(1, "Distributed systems"), Given.Story(2, "Cooking")]);

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = "stribut" }, Ct);

        response.Result.Items.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Fact]
    public async Task Search_with_no_match_returns_an_empty_page_not_everything()
    {
        var handler = CreateHandler(Given.Stories(20));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = "no-such-story" }, Ct);

        response.Result.Items.ShouldBeEmpty();
        response.Result.TotalCount.ShouldBe(0);
        response.Result.TotalPages.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_is_ignored_rather_than_matching_nothing(string? term)
    {
        var handler = CreateHandler(Given.Stories(20));

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = term, PageSize = 100 }, Ct);

        response.Result.TotalCount.ShouldBe(20);
    }

    [Fact]
    public async Task Search_is_applied_before_paging_so_the_total_reflects_matches_only()
    {
        var stories = Enumerable.Range(1, 100)
            .Select(i => Given.Story(i, i % 10 == 0 ? $"match {i}" : $"other {i}"))
            .ToList();
        var handler = CreateHandler(stories);

        var response = await handler.Handle(
            new GetNewestStoriesHandler.Query { Search = "match", PageSize = 5 }, Ct);

        response.Result.TotalCount.ShouldBe(10);
        response.Result.Items.Count.ShouldBe(5);
        response.Result.TotalPages.ShouldBe(2);
    }

    [Fact]
    public async Task Stories_without_a_url_are_returned_with_a_null_url_not_dropped()
    {
        var handler = CreateHandler([Given.Story(1, "Ask HN: how do you test?", url: null)]);

        var response = await handler.Handle(new GetNewestStoriesHandler.Query(), Ct);

        response.Result.Items.ShouldHaveSingleItem().Url.ShouldBeNull();
    }

    [Fact]
    public async Task Waits_for_the_first_load_instead_of_reading_a_possibly_cold_cache()
    {
        var handler = CreateHandler(Given.Stories(5));

        await handler.Handle(new GetNewestStoriesHandler.Query(), Ct);

        await _cache.Received(1).EnsureLoadedAsync(Arg.Any<CancellationToken>());
    }
}
