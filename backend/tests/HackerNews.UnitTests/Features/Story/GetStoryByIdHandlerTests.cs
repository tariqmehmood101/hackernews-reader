using HackerNews.Api.Exceptions;
using HackerNews.Api.Features.Story;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.UnitTests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HackerNews.UnitTests.Features.Story;

public sealed class GetStoryByIdHandlerTests
{
    private readonly IStorySnapshotCache _cache = Substitute.For<IStorySnapshotCache>();
    private readonly IHackerNewsClient _client = Substitute.For<IHackerNewsClient>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GetStoryByIdHandler CreateHandler(params StoryDto[] cached)
    {
        _cache.Current.Returns(Given.Snapshot(cached));
        return new GetStoryByIdHandler(_cache, _client);
    }

    [Fact]
    public async Task Serves_a_cached_story_without_calling_upstream()
    {
        var handler = CreateHandler(Given.Story(1), Given.Story(2));

        var response = await handler.Handle(new GetStoryByIdHandler.Query(2), Ct);

        response.Story.Id.ShouldBe(2);
        await _client.DidNotReceive().GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falls_back_to_upstream_for_a_story_that_has_aged_out_of_the_snapshot()
    {
        var handler = CreateHandler(Given.Story(1));
        _client.GetStoryAsync(500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoryDto?>(Given.Story(500, "An older story")));

        var response = await handler.Handle(new GetStoryByIdHandler.Query(500), Ct);

        response.Story.Title.ShouldBe("An older story");
    }

    [Fact]
    public async Task Reports_not_found_when_upstream_has_nothing_either()
    {
        var handler = CreateHandler();
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoryDto?>(null));

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(new GetStoryByIdHandler.Query(404), Ct));

        exception.Message.ShouldContain("404");
    }

    [Fact]
    public async Task An_unreachable_upstream_is_reported_as_unavailable_not_as_an_internal_error()
    {
        // 503 says "try again"; the opaque 500 this used to produce says "give up", which is the
        // wrong answer for a story that is merely older than the cached window.
        var handler = CreateHandler();
        var upstream = new HttpRequestException("connection refused");
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Throws(upstream);

        var exception = await Should.ThrowAsync<UpstreamUnavailableException>(
            () => handler.Handle(new GetStoryByIdHandler.Query(500), Ct));

        exception.InnerException.ShouldBeSameAs(upstream);
        exception.Message.ShouldContain("500");
    }

    /// <summary>The pipeline's own timeout elapsed before upstream answered.</summary>
    [Fact]
    public Task A_resilience_timeout_is_reported_as_unavailable() =>
        ShouldBeReportedAsUnavailable(new TimeoutRejectedException());

    /// <summary>
    /// The refresher has already been failing against this same typed client, so the breaker is
    /// open and the call never even leaves the process.
    /// </summary>
    [Fact]
    public Task An_open_circuit_is_reported_as_unavailable() =>
        ShouldBeReportedAsUnavailable(new BrokenCircuitException());

    /// <summary>HttpClient.Timeout surfaces as a cancellation the caller never asked for.</summary>
    [Fact]
    public Task A_client_timeout_is_reported_as_unavailable() =>
        ShouldBeReportedAsUnavailable(new TaskCanceledException("timed out", new TimeoutException()));

    private async Task ShouldBeReportedAsUnavailable(Exception rejection)
    {
        var handler = CreateHandler();
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Throws(rejection);

        await Should.ThrowAsync<UpstreamUnavailableException>(
            () => handler.Handle(new GetStoryByIdHandler.Query(7), Ct));
    }

    [Fact]
    public async Task A_caller_who_hangs_up_is_not_blamed_on_the_upstream()
    {
        // Cancellation the caller asked for is not an outage, so it must not be dressed up as one.
        var handler = CreateHandler();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(cts.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            () => handler.Handle(new GetStoryByIdHandler.Query(7), cts.Token));
    }

    [Fact]
    public async Task A_genuine_bug_still_surfaces_rather_than_hiding_behind_a_503()
    {
        var handler = CreateHandler();
        _client.GetStoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("a real defect"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(new GetStoryByIdHandler.Query(7), Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_a_non_positive_id(int id)
    {
        new GetStoryByIdHandler.Validator()
            .Validate(new GetStoryByIdHandler.Query(id))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Accepts_a_positive_id()
    {
        new GetStoryByIdHandler.Validator()
            .Validate(new GetStoryByIdHandler.Query(1))
            .IsValid.ShouldBeTrue();
    }
}
