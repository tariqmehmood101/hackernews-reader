using System.Diagnostics;
using MediatR;

namespace HackerNews.Api.Shared.Behaviors;

/// <summary>Logs the name and duration of every request passing through the mediator.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            logger.LogInformation(
                "{RequestName} handled in {ElapsedMilliseconds}ms.",
                requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "{RequestName} failed after {ElapsedMilliseconds}ms.",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
