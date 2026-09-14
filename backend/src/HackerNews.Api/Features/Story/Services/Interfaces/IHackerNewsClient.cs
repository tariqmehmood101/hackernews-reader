using HackerNews.Api.Features.Story.Dto;

namespace HackerNews.Api.Features.Story.Services.Interfaces;

/// <summary>Thin wrapper over the public Hacker News Firebase API.</summary>
public interface IHackerNewsClient
{
    /// <summary>Ids of the newest stories, newest first. Upstream returns at most 500.</summary>
    Task<IReadOnlyList<int>> GetNewestStoryIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A single story, or <c>null</c> when the item is missing, deleted, dead or untitled.
    /// </summary>
    Task<StoryDto?> GetStoryAsync(int id, CancellationToken cancellationToken);
}
