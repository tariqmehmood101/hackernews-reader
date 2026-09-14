using FluentValidation;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.Api.Shared.Models;
using MediatR;

namespace HackerNews.Api.Features.Story;

/// <summary>
/// Returns a page of the newest stories, optionally filtered by a search term. Reads entirely from
/// the in-memory snapshot &mdash; no upstream call happens on the request path.
/// </summary>
public sealed class GetNewestStoriesHandler(IStorySnapshotCache cache)
    : IRequestHandler<GetNewestStoriesHandler.Query, GetNewestStoriesHandler.Response>
{
    public sealed record Query : IRequest<Response>
    {
        public int Page { get; init; } = 1;

        public int PageSize { get; init; } = 20;

        /// <summary>Case-insensitive substring matched against story title and author.</summary>
        public string? Search { get; init; }
    }

    public sealed record Response(PagedResult<StoryDto> Result);

    public sealed class Validator : AbstractValidator<Query>
    {
        public const int MaxPageSize = 100;
        public const int MaxSearchLength = 100;

        public Validator()
        {
            RuleFor(query => query.Page)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Page must be 1 or greater.");

            // The brief calls out 200-on-a-page as the thing to avoid; this is the hard ceiling.
            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, MaxPageSize)
                .WithMessage($"PageSize must be between 1 and {MaxPageSize}.");

            RuleFor(query => query.Search)
                .MaximumLength(MaxSearchLength)
                .WithMessage($"Search must be {MaxSearchLength} characters or fewer.");
        }
    }

    public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
    {
        var snapshot = await cache.EnsureLoadedAsync(cancellationToken);

        var matches = Filter(snapshot.Stories, request.Search);

        // A page past the end yields no items but still reports the real total, so the client's
        // pager stays correct instead of seeing a 404.
        return new Response(PagedResult<StoryDto>.From(matches, request.Page, request.PageSize));
    }

    private static IReadOnlyList<StoryDto> Filter(IReadOnlyList<StoryDto> stories, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return stories;
        }

        var term = search.Trim();
        return stories
            .Where(story =>
                story.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (story.By?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }
}
