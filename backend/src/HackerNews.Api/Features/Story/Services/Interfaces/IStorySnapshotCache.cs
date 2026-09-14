using HackerNews.Api.Features.Story.Dto;

namespace HackerNews.Api.Features.Story.Services.Interfaces;

/// <summary>Holds the current story snapshot for lock-free reads.</summary>
public interface IStorySnapshotCache
{
    /// <summary>The current snapshot; <see cref="StorySnapshot.Empty"/> before the first refresh.</summary>
    StorySnapshot Current { get; }

    /// <summary>Atomically replaces the snapshot and releases anyone awaiting the first load.</summary>
    void Replace(StorySnapshot snapshot);

    /// <summary>
    /// Returns the snapshot, waiting for the first refresh if the cache is still cold.
    /// Throws <see cref="Exceptions.UpstreamUnavailableException"/> if it never arrives in time.
    /// </summary>
    Task<StorySnapshot> EnsureLoadedAsync(CancellationToken cancellationToken);
}
