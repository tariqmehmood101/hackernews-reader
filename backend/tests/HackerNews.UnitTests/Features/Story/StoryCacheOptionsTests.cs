using HackerNews.Api.Features.Story.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HackerNews.UnitTests.Features.Story;

public sealed class StoryCacheOptionsTests
{
    [Fact]
    public void Defaults_are_sensible_for_a_polite_client_of_a_free_api()
    {
        var options = new StoryCacheOptions();

        options.RefreshIntervalSeconds.ShouldBe(300);
        options.RetryIntervalSeconds.ShouldBe(30);
        options.InitialLoadTimeoutSeconds.ShouldBe(10);
        options.MaxConcurrentItemFetches.ShouldBe(15);
        options.FullRefreshEveryNCycles.ShouldBe(12);
        options.MinimumYieldPercent.ShouldBe(50);
    }

    [Fact]
    public void Recovers_faster_than_the_normal_cadence_after_a_failure()
    {
        var options = new StoryCacheOptions();

        options.RetryInterval.ShouldBeLessThan(options.RefreshInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(300)]
    public void Exposes_each_interval_as_a_TimeSpan(int seconds)
    {
        var options = new StoryCacheOptions
        {
            RefreshIntervalSeconds = seconds,
            RetryIntervalSeconds = seconds,
            InitialLoadTimeoutSeconds = seconds,
        };

        options.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(seconds));
        options.RetryInterval.ShouldBe(TimeSpan.FromSeconds(seconds));
        options.InitialLoadTimeout.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Binds_from_the_Cache_configuration_section()
    {
        // Every knob must be reachable from an App Service setting without a code change.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RefreshIntervalSeconds"] = "60",
                ["Cache:RetryIntervalSeconds"] = "5",
                ["Cache:InitialLoadTimeoutSeconds"] = "2",
                ["Cache:MaxConcurrentItemFetches"] = "4",
                ["Cache:FullRefreshEveryNCycles"] = "3",
                ["Cache:MinimumYieldPercent"] = "25",
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<StoryCacheOptions>(
            configuration.GetSection(StoryCacheOptions.SectionName));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<StoryCacheOptions>>().Value;

        options.RefreshIntervalSeconds.ShouldBe(60);
        options.RetryIntervalSeconds.ShouldBe(5);
        options.InitialLoadTimeoutSeconds.ShouldBe(2);
        options.MaxConcurrentItemFetches.ShouldBe(4);
        options.FullRefreshEveryNCycles.ShouldBe(3);
        options.MinimumYieldPercent.ShouldBe(25);
    }

    [Fact]
    public void Unset_keys_keep_their_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RefreshIntervalSeconds"] = "60",
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<StoryCacheOptions>(
            configuration.GetSection(StoryCacheOptions.SectionName));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<StoryCacheOptions>>().Value;

        options.RefreshIntervalSeconds.ShouldBe(60);
        options.MaxConcurrentItemFetches.ShouldBe(15);
    }

    [Fact]
    public void Section_name_matches_the_settings_file()
    {
        StoryCacheOptions.SectionName.ShouldBe("Cache");
    }
}
