using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.IntegrationTests.TestSupport;

namespace HackerNews.IntegrationTests;

/// <summary>
/// Cross-cutting policy configured in Program.cs — rate limiting, CORS, OpenAPI and the by-id
/// validation path. None of it is reachable from a unit test, so it is covered here.
/// </summary>
public sealed class ApiPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_beyond_the_rate_limit_are_rejected_with_429()
    {
        using var factory = HackerNewsApiFactory.WithRateLimit(3);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.GetAsync("/api/stories/newest?pageSize=1", Ct)).StatusCode);
        }

        statuses.Take(3).ShouldAllBe(status => status == HttpStatusCode.OK);
        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_limit_is_configurable_rather_than_hardcoded()
    {
        using var factory = HackerNewsApiFactory.WithRateLimit(50);
        using var client = factory.CreateClient();

        // The same burst that trips a limit of 3 must pass comfortably under 50.
        for (var i = 0; i < 6; i++)
        {
            (await client.GetAsync("/api/stories/newest?pageSize=1", Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_health_probe_is_never_rate_limited()
    {
        // App Service marks an instance unhealthy on a failed probe and recycles it. A probe
        // sharing a bucket with user traffic would take the instance down — and bin the warm
        // snapshot — at exactly the moment the instance is busiest.
        using var factory = HackerNewsApiFactory.WithRateLimit(2);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_forwarded_header_buys_nothing_when_no_proxy_is_trusted()
    {
        // Default configuration: nothing sits in front, so X-Forwarded-For is just something the
        // caller typed. Honouring it would hand out a fresh bucket per request for free.
        using var factory = HackerNewsApiFactory.WithRateLimit(3);
        using var client = factory.CreateClient();

        var statuses = await SendBurstAsync(client, 6, i => $"203.0.113.{i}");

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Distinct_callers_behind_a_trusted_proxy_get_their_own_buckets()
    {
        // The bug this guards: behind App Service every request arrives from the front end, so
        // partitioning on the connection address throttled the whole world as a single client.
        using var factory = HackerNewsApiFactory.BehindTrustedProxy(permitsPerMinute: 2);
        using var client = factory.CreateClient();

        // Three callers, two requests each. Nobody exceeds their own limit, so nobody is refused.
        foreach (var caller in new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3" })
        {
            var statuses = await SendBurstAsync(client, 2, _ => caller);
            statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task One_caller_behind_a_trusted_proxy_is_still_limited()
    {
        using var factory = HackerNewsApiFactory.BehindTrustedProxy(permitsPerMinute: 2);
        using var client = factory.CreateClient();

        var statuses = await SendBurstAsync(client, 5, _ => "203.0.113.7");

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Only_the_address_the_proxy_appended_identifies_the_caller()
    {
        // A caller planting a different value on the left of the header is trying to look like a
        // new client each time. Only the rightmost hop — the one the proxy itself wrote — counts,
        // which is what keeps trusting the header from becoming a way around the limiter.
        using var factory = HackerNewsApiFactory.BehindTrustedProxy(permitsPerMinute: 2);
        using var client = factory.CreateClient();

        var statuses = await SendBurstAsync(client, 5, i => $"198.51.100.{i}, 203.0.113.9");

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
    }

    private static async Task<List<HttpStatusCode>> SendBurstAsync(
        HttpClient client, int count, Func<int, string> forwardedFor)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/stories/newest?pageSize=1");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor(i));
            statuses.Add((await client.SendAsync(request, Ct)).StatusCode);
        }

        return statuses;
    }

    [Fact]
    public async Task A_production_response_over_https_carries_hsts()
    {
        using var factory = HackerNewsApiFactory.InProductionBehindTrustedProxy();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, forwardedProto: "https");

        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue();
    }

    [Fact]
    public async Task Hsts_is_silently_lost_unless_the_forwarded_scheme_is_trusted()
    {
        // The trap this pins: HSTS only emits on a response the app believes is HTTPS. Behind App
        // Service the connection to Kestrel is plain HTTP, so without Network:TrustForwardedHeaders
        // the header is quietly never sent — the hardening looks configured but does nothing.
        using var factory = HackerNewsApiFactory.InProductionBehindTrustedProxy(trustProxy: false);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, forwardedProto: "https");

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Development_is_left_alone_so_local_http_still_works()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, forwardedProto: "https");

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task A_plain_http_request_is_not_redirected_into_a_loop()
    {
        // There is deliberately no UseHttpsRedirection: behind a proxy whose scheme we do not
        // trust, redirecting would bounce back as HTTP forever. The platform owns the redirect.
        using var factory = HackerNewsApiFactory.InProductionBehindTrustedProxy(trustProxy: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stories/newest?pageSize=1", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string forwardedProto)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stories/newest?pageSize=1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.50");

        // HSTS deliberately skips localhost — pinning a dev machine to HTTPS would be a nuisance
        // to undo — and TestServer is localhost, so a real host is needed to observe the header.
        request.Headers.Host = "hackernews.example";

        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task An_origin_that_is_not_configured_gets_no_cors_grant()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stories/newest?pageSize=1");
        request.Headers.Add("Origin", "https://not-our-frontend.example");

        var response = await client.SendAsync(request, Ct);

        // The request still succeeds server-side; the browser is what enforces the absence.
        response.EnsureSuccessStatusCode();
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task A_preflight_from_the_configured_origin_is_approved()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/stories/newest");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, Ct);

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldContain("http://localhost:4200");
    }

    [Fact]
    public async Task The_openapi_document_is_served_in_development()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        document.GetProperty("paths").TryGetProperty("/api/stories/newest", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_story_id_is_rejected_before_any_lookup(int id)
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/stories/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors").TryGetProperty("Id", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_routes_return_404_rather_than_an_error_page()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/nope", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Responses_are_json_by_default()
    {
        using var factory = HackerNewsApiFactory.Healthy();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stories/newest?pageSize=1", Ct);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }
}
