using HackerNews.Api.Shared.Behaviors;
using HackerNews.UnitTests.TestSupport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HackerNews.UnitTests.Shared;

public sealed class LoggingBehaviorTests
{
    private readonly RecordingLogger<LoggingBehavior<Request, string>> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Request(string Name);

    private LoggingBehavior<Request, string> CreateBehavior() => new(_logger);

    private static RequestHandlerDelegate<string> Returning(string value) => _ => Task.FromResult(value);

    private static RequestHandlerDelegate<string> Throwing(Exception exception) =>
        _ => Task.FromException<string>(exception);

    [Fact]
    public async Task Passes_the_handlers_response_straight_through()
    {
        var result = await CreateBehavior().Handle(new Request("x"), Returning("handled"), Ct);

        result.ShouldBe("handled");
    }

    [Fact]
    public async Task Invokes_the_handler_exactly_once()
    {
        var calls = 0;
        RequestHandlerDelegate<string> next = _ =>
        {
            calls++;
            return Task.FromResult("ok");
        };

        await CreateBehavior().Handle(new Request("x"), next, Ct);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Logs_the_request_name_and_duration_on_success()
    {
        await CreateBehavior().Handle(new Request("x"), Returning("ok"), Ct);

        _logger.Logged(LogLevel.Information, nameof(Request)).ShouldBeTrue();
        _logger.Logged(LogLevel.Information, "ms").ShouldBeTrue();
    }

    [Fact]
    public async Task Rethrows_the_handlers_exception_unchanged()
    {
        var boom = new InvalidOperationException("handler blew up");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateBehavior().Handle(new Request("x"), Throwing(boom), Ct));

        // The pipeline must not swallow or wrap it — GlobalExceptionHandler maps the real type.
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Logs_a_warning_when_the_handler_fails()
    {
        var boom = new InvalidOperationException("handler blew up");

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateBehavior().Handle(new Request("x"), Throwing(boom), Ct));

        _logger.Logged(LogLevel.Warning, nameof(Request)).ShouldBeTrue();
        _logger.Entries.ShouldContain(entry => entry.Exception == boom);
    }

    [Fact]
    public async Task Does_not_log_a_success_line_when_the_handler_fails()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateBehavior().Handle(new Request("x"), Throwing(new InvalidOperationException()), Ct));

        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Forwards_the_cancellation_token_to_the_handler()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;
        RequestHandlerDelegate<string> next = token =>
        {
            received = token;
            return Task.FromResult("ok");
        };

        await CreateBehavior().Handle(new Request("x"), next, cts.Token);

        received.ShouldBe(cts.Token);
    }
}
