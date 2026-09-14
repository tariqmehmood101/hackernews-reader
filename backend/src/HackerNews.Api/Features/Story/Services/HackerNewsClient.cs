using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services.Interfaces;

namespace HackerNews.Api.Features.Story.Services;

/// <inheritdoc cref="IHackerNewsClient"/>
public sealed class HackerNewsClient(HttpClient httpClient, ILogger<HackerNewsClient> logger)
    : IHackerNewsClient
{
    public async Task<IReadOnlyList<int>> GetNewestStoryIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await httpClient.GetFromJsonAsync<int[]>("newstories.json", cancellationToken);
        return ids ?? [];
    }

    public async Task<StoryDto?> GetStoryAsync(int id, CancellationToken cancellationToken)
    {
        var item = await httpClient.GetFromJsonAsync<HackerNewsItem>(
            $"item/{id}.json", cancellationToken);

        // Upstream answers "null" for ids that have been purged; deleted/dead items are tombstones.
        if (item is null || item.Deleted || item.Dead)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            logger.LogDebug("Skipping item {ItemId}: no title (type {ItemType})", id, item.Type);
            return null;
        }

        return new StoryDto(
            Id: item.Id,
            Title: item.Title,
            // Ask HN / Show HN text posts carry no url. Preserved as null, never coerced to "".
            Url: string.IsNullOrWhiteSpace(item.Url) ? null : item.Url,
            By: item.By,
            Time: DateTimeOffset.FromUnixTimeSeconds(item.Time),
            Score: item.Score,
            Descendants: item.Descendants);
    }

    /// <summary>
    /// Wire shape of an upstream item. Every field beyond <c>id</c> is optional per the HN docs,
    /// so all of them are defaulted rather than required.
    /// </summary>
    private sealed record HackerNewsItem
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("by")] public string? By { get; init; }
        [JsonPropertyName("time")] public long Time { get; init; }
        [JsonPropertyName("score")] public int Score { get; init; }
        [JsonPropertyName("descendants")] public int Descendants { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("dead")] public bool Dead { get; init; }
        [JsonPropertyName("deleted")] public bool Deleted { get; init; }
    }
}
