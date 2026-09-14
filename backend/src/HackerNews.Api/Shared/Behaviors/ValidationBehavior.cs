using FluentValidation;
using MediatR;

namespace HackerNews.Api.Shared.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for a request before its handler executes,
/// so handlers can assume valid input. Failures become a 400 in
/// <see cref="Exceptions.GlobalExceptionHandler"/>.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IReadOnlyList<IValidator<TRequest>> ?? validators.ToList();
        if (applicable.Count == 0)
        {
            return await next(cancellationToken);
        }

        // Each validator gets its own context. A shared ValidationContext accumulates failures in
        // a list every result then wraps, so reusing one duplicates messages across validators —
        // and mutating it from concurrent validators is a race besides.
        var results = await Task.WhenAll(applicable.Select(validator =>
            validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));

        var failures = results.SelectMany(result => result.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
