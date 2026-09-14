using FluentValidation;
using FluentValidation.Results;
using HackerNews.Api.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HackerNews.UnitTests.Exceptions;

public sealed class GlobalExceptionHandlerTests
{
    private readonly IProblemDetailsService _problemDetailsService =
        Substitute.For<IProblemDetailsService>();

    private ProblemDetailsContext? _written;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GlobalExceptionHandlerTests() =>
        _problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => _written = context))
            .Returns(ValueTask.FromResult(true));

    private async Task<HttpContext> HandleAsync(Exception exception)
    {
        var handler = new GlobalExceptionHandler(
            _problemDetailsService, NullLogger<GlobalExceptionHandler>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/stories/newest";

        (await handler.TryHandleAsync(httpContext, exception, Ct)).ShouldBeTrue();
        return httpContext;
    }

    [Fact]
    public async Task Maps_a_validation_failure_to_400_with_the_offending_fields()
    {
        var exception = new ValidationException([
            new ValidationFailure("PageSize", "PageSize must be between 1 and 100."),
            new ValidationFailure("Page", "Page must be 1 or greater.")
        ]);

        var httpContext = await HandleAsync(exception);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        _written!.ProblemDetails.Status.ShouldBe(StatusCodes.Status400BadRequest);

        var errors = _written.ProblemDetails.Extensions["errors"].ShouldBeOfType<Dictionary<string, string[]>>();
        errors["PageSize"].ShouldContain("PageSize must be between 1 and 100.");
        errors["Page"].ShouldContain("Page must be 1 or greater.");
    }

    [Fact]
    public async Task Groups_several_failures_on_the_same_field()
    {
        var exception = new ValidationException([
            new ValidationFailure("Page", "first"),
            new ValidationFailure("Page", "second")
        ]);

        await HandleAsync(exception);

        var errors = (Dictionary<string, string[]>)_written!.ProblemDetails.Extensions["errors"]!;
        errors["Page"].ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Maps_a_missing_resource_to_404_and_keeps_the_message()
    {
        var httpContext = await HandleAsync(new NotFoundException("Story 42 was not found."));

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _written!.ProblemDetails.Detail.ShouldBe("Story 42 was not found.");
    }

    [Fact]
    public async Task Maps_an_unreachable_upstream_to_503()
    {
        var httpContext = await HandleAsync(new UpstreamUnavailableException("Feed not loaded yet."));

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        _written!.ProblemDetails.Detail.ShouldBe("Feed not loaded yet.");
    }

    [Fact]
    public async Task Maps_anything_else_to_500_without_leaking_internals()
    {
        var httpContext = await HandleAsync(
            new InvalidOperationException("connection string user=sa;password=hunter2"));

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        _written!.ProblemDetails.Detail.ShouldBeNull();
        _written.ProblemDetails.Title.ShouldBe("An unexpected error occurred.");
        _written.ProblemDetails.Extensions.ShouldNotContainKey("errors");
    }

    [Fact]
    public async Task Records_the_route_that_failed()
    {
        await HandleAsync(new NotFoundException("nope"));

        _written!.ProblemDetails.Instance.ShouldBe("GET /api/stories/newest");
    }
}
