using FluentValidation;
using HackerNews.Api.Exceptions;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services.Interfaces;
using MediatR;
using Polly;

namespace HackerNews.Api.Features.Story;

/// <summary>
/// Returns a single story. Served from the snapshot when possible, otherwise fetched upstream so
/// that stories which have aged out of the newest-500 window still resolve.
/// </summary>
public sealed class GetStoryByIdHandler(IStorySnapshotCache cache, IHackerNewsClient client)
    : IRequestHandler<GetStoryByIdHandler.Query, GetStoryByIdHandler.Response>
{
    public sealed record Query(int Id) : IRequest<Response>;

    public sealed record Response(StoryDto Story);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() =>
            RuleFor(query => query.Id)
                .GreaterThan(0)
                .WithMessage("Id must be a positive number.");
    }

    public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
    {
        var cached = cache.Current.Stories.FirstOrDefault(story => story.Id == request.Id);
        if (cached is not null)
        {
            return new Response(cached);
        }

        // Nothing cached, so upstream is the only source left. "We could not reach Hacker News"
        // and "that story does not exist" are different answers, and collapsing the first into an
        // opaque 500 tells the caller to stop retrying when retrying is exactly what would work.
        StoryDto? story;
        try
        {
            story = await client.GetStoryAsync(request.Id, cancellationToken);
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, cancellationToken))
        {
            throw new UpstreamUnavailableException(
                $"Story {request.Id} is not in the cached feed and Hacker News could not be " +
                "reached. Please retry shortly.",
                ex);
        }

        return new Response(story ?? throw new NotFoundException($"Story {request.Id} was not found."));
    }

    /// <summary>
    /// True when the call failed because Hacker News could not be reached. A caller who hangs up
    /// mid-request also cancels, but that is the client's own doing rather than an upstream fault,
    /// so its cancellation is deliberately excluded and left to propagate.
    /// </summary>
    private static bool IsUpstreamFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception switch
        {
            // Transport failure, or a non-success status that GetFromJsonAsync turned into a throw.
            HttpRequestException => true,

            // Polly refused to run the call: the resilience pipeline's timeout elapsed, or the
            // circuit is already open because the background refresher has been failing against
            // this same typed client.
            ExecutionRejectedException => true,

            // HttpClient.Timeout surfaces as a cancellation that nobody asked for.
            OperationCanceledException => true,

            _ => false
        };
}
