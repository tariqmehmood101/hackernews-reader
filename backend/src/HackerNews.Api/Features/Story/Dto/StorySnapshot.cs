namespace HackerNews.Api.Features.Story.Dto;

/// <summary>
/// An immutable point-in-time view of the newest stories. Swapped atomically by the refresher so
/// readers never observe a partially built list and never need a lock.
/// </summary>
public sealed record StorySnapshot(IReadOnlyList<StoryDto> Stories, DateTimeOffset RefreshedAt)
{
    public static StorySnapshot Empty { get; } = new([], DateTimeOffset.MinValue);

    /// <summary>False until the first successful refresh has completed.</summary>
    public bool IsLoaded => RefreshedAt != DateTimeOffset.MinValue;
}
