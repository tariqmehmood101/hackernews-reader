using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.IntegrationTests.TestSupport;

namespace HackerNews.IntegrationTests;

/// <summary>
/// Covers the two behaviours the brief cares about beyond the happy path: that the feed is really
/// cached, and that an unreachable upstream is reported honestly rather than as an empty list.
/// </summary>
public sealed class CachingAndResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_are_served_from_the_snapshot_without_re_hitting_upstream()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/stories/newest", Ct)).EnsureSuccessStatusCode();
        var itemRequestsAfterWarmUp = factory.Upstream.ItemRequests;
        itemRequestsAfterWarmUp.ShouldBe(FakeHackerNews.StoryCount);

        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync($"/api/stories/newest?page={i + 1}&pageSize=5", Ct))
                .EnsureSuccessStatusCode();
        }

        // The whole point of the snapshot: reads cost nothing upstream.
        factory.Upstream.ItemRequests.ShouldBe(itemRequestsAfterWarmUp);
        factory.Upstream.IdListRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_story_already_in_the_snapshot_is_not_re_fetched_by_the_by_id_endpoint()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/stories/newest", Ct)).EnsureSuccessStatusCode();
        var before = factory.Upstream.ItemRequests;

        (await client.GetAsync("/api/stories/5", Ct)).EnsureSuccessStatusCode();

        factory.Upstream.ItemRequests.ShouldBe(before);
    }

    [Fact]
    public async Task An_unreachable_upstream_returns_503_rather_than_an_empty_list()
    {
        using var factory = HackerNewsApiFactory.UpstreamDown();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stories/newest", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetInt32().ShouldBe(503);
        body.GetProperty("title").GetString().ShouldBe("Upstream service unavailable.");
    }

    [Fact]
    public async Task Health_still_reports_healthy_while_the_upstream_is_down()
    {
        // The probe answers for the process, not the third party; App Service must not recycle
        // the instance just because Hacker News is having a bad day.
        using var factory = HackerNewsApiFactory.UpstreamDown();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validation_is_applied_before_the_cache_is_consulted()
    {
        // A bad request should fail fast with 400 even when there is no data to serve.
        using var factory = HackerNewsApiFactory.UpstreamDown();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stories/newest?pageSize=500", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_configured_frontend_origin_is_allowed_by_cors()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stories/newest?pageSize=1");
        request.Headers.Add("Origin", "http://localhost:4200");

        var response = await client.SendAsync(request, Ct);

        response.EnsureSuccessStatusCode();
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).ShouldBeTrue();
        values!.ShouldContain("http://localhost:4200");
    }
}
