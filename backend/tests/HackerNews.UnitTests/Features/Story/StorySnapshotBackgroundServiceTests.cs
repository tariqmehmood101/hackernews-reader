using HackerNews.Api.Features.Story.Options;
using HackerNews.Api.Features.Story.Services;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HackerNews.UnitTests.Features.Story;

/// <summary>
/// The hosted service owns only the schedule, so these cover the loop itself: that it keeps going,
/// survives a failing pass, and shuts down promptly.
/// </summary>
public sealed class StorySnapshotBackgroundServiceTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly IStorySnapshotRefresher _refresher = Substitute.For<IStorySnapshotRefresher>();
    private readonly RecordingLogger<StorySnapshotBackgroundService> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StorySnapshotBackgroundService CreateService(
        int refreshIntervalSeconds = 0,
        int retryIntervalSeconds = 0) =>
        new(
            _refresher,
            Options.Create(new StoryCacheOptions
            {
                RefreshIntervalSeconds = refreshIntervalSeconds,
                RetryIntervalSeconds = retryIntervalSeconds,
            }),
            _logger);

    /// <summary>Runs the service until its loop exits, failing rather than hanging.</summary>
    private static async Task RunUntilStoppedAsync(
        StorySnapshotBackgroundService service, CancellationTokenSource stop)
    {
        await service.StartAsync(stop.Token);
        await service.ExecuteTask!.WaitAsync(StopTimeout, Ct);
    }

    [Fact]
    public async Task Refreshes_repeatedly_until_it_is_stopped()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref calls) >= 3)
            {
                stop.Cancel();
            }
            return Task.CompletedTask;
        });

        await RunUntilStoppedAsync(CreateService(), stop);

        calls.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_failing_pass_does_not_stop_the_loop()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                // A refresh failure must never tear the host down.
                throw new HttpRequestException("upstream down");
            }
            if (call >= 3)
            {
                stop.Cancel();
            }
            return Task.CompletedTask;
        });

        await RunUntilStoppedAsync(CreateService(), stop);

        calls.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Logs_the_failure_rather_than_swallowing_it_silently()
    {
        using var stop = new CancellationTokenSource();
        var boom = new HttpRequestException("upstream down");
        var calls = 0;
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref calls) >= 2)
            {
                stop.Cancel();
            }
            throw boom;
        });

        await RunUntilStoppedAsync(CreateService(), stop);

        _logger.Logged(LogLevel.Error, "failed").ShouldBeTrue();
        _logger.Entries.ShouldContain(entry => entry.Exception == boom);
    }

    [Fact]
    public async Task Stops_promptly_while_waiting_for_the_next_interval()
    {
        var inDelay = new TaskCompletionSource();
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            inDelay.TrySetResult();
            return Task.CompletedTask;
        });

        // An hour between passes: if shutdown were not observed, this would time out.
        var service = CreateService(refreshIntervalSeconds: 3600);
        await service.StartAsync(CancellationToken.None);
        await inDelay.Task.WaitAsync(StopTimeout, Ct);

        // StopAsync is the path the host actually takes on shutdown.
        await service.StopAsync(Ct).WaitAsync(StopTimeout, Ct);

        service.ExecuteTask!.IsCompleted.ShouldBeTrue();
        _logger.Logged(LogLevel.Information, "stopped").ShouldBeTrue();
    }

    [Fact]
    public async Task Shutdown_mid_refresh_is_not_reported_as_a_failure()
    {
        using var stop = new CancellationTokenSource();
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            // Exactly what a cancelled HTTP call does on the way down.
            throw new OperationCanceledException(stop.Token);
        });

        var service = CreateService();
        await RunUntilStoppedAsync(service, stop);

        service.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Announces_its_start_and_stop()
    {
        using var stop = new CancellationTokenSource();
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.CompletedTask;
        });

        await RunUntilStoppedAsync(CreateService(), stop);

        _logger.Logged(LogLevel.Information, "starting").ShouldBeTrue();
        _logger.Logged(LogLevel.Information, "stopped").ShouldBeTrue();
    }

    [Fact]
    public async Task Passes_its_stopping_token_to_the_refresher()
    {
        using var stop = new CancellationTokenSource();
        CancellationToken received = default;
        _refresher.RefreshAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            received = call.Arg<CancellationToken>();
            stop.Cancel();
            return Task.CompletedTask;
        });

        await RunUntilStoppedAsync(CreateService(), stop);

        received.CanBeCanceled.ShouldBeTrue();
        received.IsCancellationRequested.ShouldBeTrue();
    }
}
