using System.Globalization;
using HackerNews.Api.Features.Story.Services;
using HackerNews.Api.Features.Story.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HackerNews.IntegrationTests.TestSupport;

/// <summary>
/// Boots the real application &mdash; real controllers, real mediator pipeline, real validators,
/// real exception handler &mdash; with only the outbound HTTP handler swapped for
/// <see cref="FakeHackerNews"/>.
/// </summary>
internal sealed class HackerNewsApiFactory(
    FakeHackerNews upstream,
    IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public FakeHackerNews Upstream { get; } = upstream;

    public static HackerNewsApiFactory Healthy() => new(FakeHackerNews.Healthy());

    /// <summary>A host whose fixed-window rate limiter allows only <paramref name="permitsPerMinute"/> requests.</summary>
    public static HackerNewsApiFactory WithRateLimit(int permitsPerMinute) =>
        new(FakeHackerNews.Healthy(),
            new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimitPerMinute"] =
                    permitsPerMinute.ToString(CultureInfo.InvariantCulture),
            });

    /// <summary>
    /// A host configured as it would be behind Azure's front end: <c>X-Forwarded-For</c> is
    /// trusted, so the caller the rate limiter sees is the one the proxy names.
    /// </summary>
    public static HackerNewsApiFactory BehindTrustedProxy(int permitsPerMinute) =>
        new(FakeHackerNews.Healthy(),
            new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimitPerMinute"] =
                    permitsPerMinute.ToString(CultureInfo.InvariantCulture),
                ["Network:TrustForwardedHeaders"] = "true",
            });

    /// <summary>
    /// A host booted as it runs in production, behind a trusted proxy. Development is exempt from
    /// HSTS, so anything asserting transport hardening has to leave it.
    /// </summary>
    public static HackerNewsApiFactory InProductionBehindTrustedProxy(bool trustProxy = true) =>
        new(FakeHackerNews.Healthy(),
            new Dictionary<string, string?>
            {
                ["Network:TrustForwardedHeaders"] = trustProxy ? "true" : "false",
            })
        { Environment = Environments.Production };

    /// <summary>Which environment the host boots as; Development unless a factory overrides it.</summary>
    private string Environment { get; init; } = Environments.Development;

    /// <summary>
    /// Upstream is down and the first-load wait is zero, so requests fail fast rather than
    /// blocking the test for the production timeout.
    /// </summary>
    public static HackerNewsApiFactory UpstreamDown() =>
        new(FakeHackerNews.Unreachable(),
            new Dictionary<string, string?> { ["Cache:InitialLoadTimeoutSeconds"] = "0" });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["HackerNews:BaseUrl"] = "https://hacker-news.test/v0/",
            // Long enough that the first request reliably waits for the initial refresh instead
            // of racing it, which is what makes these tests deterministic.
            ["Cache:InitialLoadTimeoutSeconds"] = "30",
            // One pass per test run; the refresh cadence is not what is under test here.
            ["Cache:RefreshIntervalSeconds"] = "3600",
            ["Cache:MaxConcurrentItemFetches"] = "8",
            ["Cache:FullRefreshEveryNCycles"] = "0",
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        builder.ConfigureTestServices(services =>
            services
                .AddHttpClient<IHackerNewsClient, HackerNewsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Upstream));
    }
}
