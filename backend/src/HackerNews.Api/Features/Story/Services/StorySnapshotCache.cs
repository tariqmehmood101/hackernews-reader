using HackerNews.Api.Exceptions;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Options;
using HackerNews.Api.Features.Story.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Features.Story.Services;

/// <inheritdoc cref="IStorySnapshotCache"/>
public sealed class StorySnapshotCache : IStorySnapshotCache
{
    private readonly TaskCompletionSource _firstLoad =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TimeSpan _initialLoadTimeout;
    private StorySnapshot _current = StorySnapshot.Empty;

    public StorySnapshotCache(IOptions<StoryCacheOptions> options) =>
        _initialLoadTimeout = options.Value.InitialLoadTimeout;

    public StorySnapshot Current => Volatile.Read(ref _current);

    public void Replace(StorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _current, snapshot);
        _firstLoad.TrySetResult();
    }

    public async Task<StorySnapshot> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var snapshot = Current;
        if (snapshot.IsLoaded)
        {
            return snapshot;
        }

        // Cold start: the background refresher is probably mid-flight. Wait briefly rather than
        // answering with an empty list, which a client cannot distinguish from "no stories".
        try
        {
            await _firstLoad.Task.WaitAsync(_initialLoadTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new UpstreamUnavailableException(
                "The Hacker News feed has not loaded yet. Please retry shortly.", ex);
        }

        return Current;
    }
}
