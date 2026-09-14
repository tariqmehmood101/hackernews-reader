using HackerNews.Api.Features.Story.Options;
using HackerNews.Api.Features.Story.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Features.Story.Services;

/// <summary>
/// Owns only the schedule. All refresh logic lives in <see cref="IStorySnapshotRefresher"/> so it
/// stays testable without a host. Implements the framework's <c>IHostedService</c> via
/// <see cref="BackgroundService"/>, which is why it has no hand-rolled interface of its own.
/// </summary>
public sealed class StorySnapshotBackgroundService(
    IStorySnapshotRefresher refresher,
    IOptions<StoryCacheOptions> options,
    ILogger<StorySnapshotBackgroundService> logger) : BackgroundService
{
    private readonly StoryCacheOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Story snapshot refresh starting; interval {Interval}.", _options.RefreshInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await refresher.RefreshAsync(stoppingToken);
                delay = _options.RefreshInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a refresh failure tear down the host: log, keep the old snapshot,
                // and come back sooner than the normal cadence.
                logger.LogError(ex, "Story snapshot refresh failed; retrying in {Delay}.",
                    _options.RetryInterval);
                delay = _options.RetryInterval;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Story snapshot refresh stopped.");
    }
}
