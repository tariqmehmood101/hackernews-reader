using HackerNews.Api.Shared.Models;

namespace HackerNews.UnitTests.Shared;

public sealed class PagedResultTests
{
    private static readonly int[] Source = Enumerable.Range(1, 25).ToArray();

    [Theory]
    [InlineData(1, 10, 10, 1)]
    [InlineData(2, 10, 10, 11)]
    [InlineData(3, 10, 5, 21)]
    public void Takes_the_right_slice(int page, int pageSize, int expectedCount, int expectedFirst)
    {
        var result = PagedResult<int>.From(Source, page, pageSize);

        result.Items.Count.ShouldBe(expectedCount);
        result.Items[0].ShouldBe(expectedFirst);
        result.TotalCount.ShouldBe(25);
    }

    [Theory]
    [InlineData(25, 10, 3)]
    [InlineData(20, 10, 2)]
    [InlineData(1, 10, 1)]
    [InlineData(0, 10, 0)]
    public void Rounds_total_pages_up(int total, int pageSize, int expected)
    {
        PagedResult<int>.From(Enumerable.Range(1, total).ToArray(), 1, pageSize)
            .TotalPages.ShouldBe(expected);
    }

    [Theory]
    // 21,474,837 is the largest page whose (page - 1) * 100 still fits in an int; the next one
    // wraps negative, and int.MaxValue wraps to a small negative number.
    [InlineData(21_474_837)]
    [InlineData(21_474_838)]
    [InlineData(int.MaxValue)]
    public void A_page_far_past_the_end_is_empty_rather_than_wrapping_back_to_page_one(int page)
    {
        var result = PagedResult<int>.From(Source, page, 100);

        // The overflow this guards: the negative skip was treated as zero, so the API served
        // page 1's rows under a far-off page number instead of the documented empty page.
        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(25);
        result.Page.ShouldBe(page);
    }

    [Fact]
    public void The_last_page_that_still_holds_items_is_unaffected_by_the_overflow_guard()
    {
        // Boundary in the other direction: skip lands exactly one short of the end.
        var result = PagedResult<int>.From(Source, 5, 6);

        result.Items.ShouldBe([25]);
    }

    [Fact]
    public void Reports_navigation_flags_from_the_current_position()
    {
        PagedResult<int>.From(Source, 2, 10).HasPreviousPage.ShouldBeTrue();
        PagedResult<int>.From(Source, 1, 10).HasPreviousPage.ShouldBeFalse();
        PagedResult<int>.From(Source, 2, 10).HasNextPage.ShouldBeTrue();
        PagedResult<int>.From(Source, 3, 10).HasNextPage.ShouldBeFalse();
    }
}
