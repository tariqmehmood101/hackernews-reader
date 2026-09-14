using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.IntegrationTests.TestSupport;

namespace HackerNews.IntegrationTests;

/// <summary>
/// End-to-end over the real HTTP stack: routing, model binding, the MediatR pipeline, validation
/// and the ProblemDetails exception handler.
/// </summary>
public sealed class StoriesEndpointTests : IClassFixture<StoriesEndpointTests.Fixture>
{
    private readonly HttpClient _client;

    public StoriesEndpointTests(Fixture fixture) => _client = fixture.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One host for the whole class; the fake upstream keeps it deterministic.</summary>
    public sealed class Fixture : IDisposable
    {
        private readonly HackerNewsApiFactory _factory = HackerNewsApiFactory.Healthy();

        public Fixture() => Client = _factory.CreateClient();

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await _client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Newest_returns_a_page_of_stories()
    {
        var body = await GetJsonAsync("/api/stories/newest?page=1&pageSize=10");

        body.GetProperty("items").GetArrayLength().ShouldBe(10);
        body.GetProperty("totalCount").GetInt32().ShouldBe(FakeHackerNews.StoryCount);
        body.GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(10);
        body.GetProperty("totalPages").GetInt32().ShouldBe(3);
        body.GetProperty("hasPreviousPage").GetBoolean().ShouldBeFalse();
        body.GetProperty("hasNextPage").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Newest_serialises_as_camel_case()
    {
        var body = await GetJsonAsync("/api/stories/newest?pageSize=1");

        var story = body.GetProperty("items")[0];
        foreach (var name in new[] { "id", "title", "url", "by", "time", "score", "descendants" })
        {
            story.TryGetProperty(name, out _).ShouldBeTrue($"expected camelCase property '{name}'");
        }
    }

    [Fact]
    public async Task Newest_orders_newest_first()
    {
        var body = await GetJsonAsync("/api/stories/newest?pageSize=5");

        var times = body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("time").GetDateTimeOffset())
            .ToList();

        times.ShouldBe(times.OrderByDescending(t => t).ToList());
    }

    [Fact]
    public async Task Paging_moves_through_the_feed_without_repeating_a_story()
    {
        var first = await GetJsonAsync("/api/stories/newest?page=1&pageSize=10");
        var second = await GetJsonAsync("/api/stories/newest?page=2&pageSize=10");

        static int[] Ids(JsonElement body) => body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32()).ToArray();

        Ids(first).Intersect(Ids(second)).ShouldBeEmpty();
        second.GetProperty("hasPreviousPage").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_keeps_the_real_total()
    {
        var body = await GetJsonAsync("/api/stories/newest?page=99&pageSize=10");

        body.GetProperty("items").GetArrayLength().ShouldBe(0);
        body.GetProperty("totalCount").GetInt32().ShouldBe(FakeHackerNews.StoryCount);
    }

    [Fact]
    public async Task A_page_number_big_enough_to_overflow_the_offset_is_still_empty()
    {
        // The validator caps PageSize but deliberately not Page, so a page far past the end stays
        // a valid request that answers with an empty page. It must not quietly serve page 1.
        var body = await GetJsonAsync($"/api/stories/newest?page={int.MaxValue}&pageSize=100");

        body.GetProperty("items").GetArrayLength().ShouldBe(0);
        body.GetProperty("totalCount").GetInt32().ShouldBe(FakeHackerNews.StoryCount);
        body.GetProperty("page").GetInt32().ShouldBe(int.MaxValue);
    }

    [Fact]
    public async Task Search_filters_the_feed()
    {
        var body = await GetJsonAsync("/api/stories/newest?search=rust&pageSize=100");

        body.GetProperty("totalCount").GetInt32().ShouldBe(FakeHackerNews.RustStoryCount);
        body.GetProperty("items").EnumerateArray()
            .ShouldAllBe(item => item.GetProperty("title").GetString()!.Contains("Rust"));
    }

    [Fact]
    public async Task Search_matches_the_author_too()
    {
        var body = await GetJsonAsync("/api/stories/newest?search=author7&pageSize=100");

        body.GetProperty("totalCount").GetInt32().ShouldBe(1);
        body.GetProperty("items")[0].GetProperty("id").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task Search_with_no_match_returns_an_empty_page()
    {
        var body = await GetJsonAsync("/api/stories/newest?search=definitely-not-here");

        body.GetProperty("items").GetArrayLength().ShouldBe(0);
        body.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Stories_without_a_hyperlink_come_back_with_a_null_url()
    {
        var body = await GetJsonAsync("/api/stories/newest?pageSize=100");

        var linkless = body.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("url").ValueKind == JsonValueKind.Null)
            .ToList();

        linkless.Count.ShouldBe(FakeHackerNews.LinklessStoryCount);
        // They must still carry a title, or the UI has nothing to render.
        linkless.ShouldAllBe(item => item.GetProperty("title").GetString()!.Length > 0);
    }

    [Theory]
    [InlineData("pageSize=500", "PageSize")]
    [InlineData("pageSize=0", "PageSize")]
    [InlineData("page=0", "Page")]
    [InlineData("page=-1", "Page")]
    public async Task Invalid_paging_is_rejected_with_problem_details(string query, string field)
    {
        var response = await _client.GetAsync($"/api/stories/newest?{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("errors").TryGetProperty(field, out var messages).ShouldBeTrue();
        messages.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task An_over_long_search_term_is_rejected()
    {
        var response = await _client.GetAsync(
            $"/api/stories/newest?search={new string('x', 101)}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_known_story_can_be_fetched_by_id()
    {
        var body = await GetJsonAsync("/api/stories/3");

        body.GetProperty("id").GetInt32().ShouldBe(3);
        body.GetProperty("title").GetString().ShouldBe(FakeHackerNews.TitleFor(3));
    }

    [Fact]
    public async Task An_unknown_story_id_returns_404_problem_details()
    {
        var response = await _client.GetAsync("/api/stories/999999", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetInt32().ShouldBe(404);
        body.GetProperty("detail").GetString()!.ShouldContain("999999");
    }

    [Fact]
    public async Task A_non_numeric_story_id_does_not_match_the_route()
    {
        var response = await _client.GetAsync("/api/stories/not-a-number", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_reports_healthy()
    {
        var response = await _client.GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }
}
