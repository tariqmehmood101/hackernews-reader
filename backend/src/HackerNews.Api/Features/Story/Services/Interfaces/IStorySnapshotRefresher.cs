namespace HackerNews.Api.Features.Story.Services.Interfaces;

/// <summary>
/// Performs a single refresh pass. Deliberately separate from the hosted service that schedules
/// it, so the logic can be exercised in tests without a host, timers or waits.
/// </summary>
public interface IStorySnapshotRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken);
}
