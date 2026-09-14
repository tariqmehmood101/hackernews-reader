using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Options;
using Microsoft.Extensions.Options;

namespace HackerNews.UnitTests.TestSupport;

/// <summary>Small builders so each test states only what it actually cares about.</summary>
internal static class Given
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static StoryDto Story(
        int id,
        string? title = null,
        string? url = "https://example.com",
        string? by = "author",
        int minutesOld = 0) =>
        new(id, title ?? $"Story {id}", url, by, Epoch.AddMinutes(-minutesOld), Score: id, Descendants: 0);

    /// <summary>Stories ordered newest first, ids 1..count.</summary>
    public static IReadOnlyList<StoryDto> Stories(int count) =>
        Enumerable.Range(1, count).Select(i => Story(i, minutesOld: i)).ToList();

    public static StorySnapshot Snapshot(IReadOnlyList<StoryDto> stories) =>
        new(stories, Epoch);

    public static IOptions<StoryCacheOptions> CacheOptions(
        int initialLoadTimeoutSeconds = 10,
        int maxConcurrentItemFetches = 4,
        int fullRefreshEveryNCycles = 0,
        int minimumYieldPercent = 50) =>
        Options.Create(new StoryCacheOptions
        {
            InitialLoadTimeoutSeconds = initialLoadTimeoutSeconds,
            MaxConcurrentItemFetches = maxConcurrentItemFetches,
            FullRefreshEveryNCycles = fullRefreshEveryNCycles,
            MinimumYieldPercent = minimumYieldPercent
        });
}
