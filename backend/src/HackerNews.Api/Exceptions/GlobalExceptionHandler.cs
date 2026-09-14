using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HackerNews.Api.Exceptions;

/// <summary>
/// Translates every unhandled exception into an RFC 7807 ProblemDetails response. Internal detail
/// is logged, never returned, for anything we did not deliberately map.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail, errors) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {StatusCode}: {Title}", statusCode, title);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int StatusCode, string Title, string? Detail, IDictionary<string, string[]>? Errors)
        Map(Exception exception) => exception switch
    {
        ValidationException validation => (
            StatusCodes.Status400BadRequest,
            "One or more validation errors occurred.",
            null,
            validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray())),

        NotFoundException notFound => (
            StatusCodes.Status404NotFound, "Resource not found.", notFound.Message, null),

        UpstreamUnavailableException unavailable => (
            StatusCodes.Status503ServiceUnavailable,
            "Upstream service unavailable.",
            unavailable.Message,
            null),

        // Deliberately opaque: the real message is logged above, not returned to the caller.
        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            null,
            null)
    };
}
