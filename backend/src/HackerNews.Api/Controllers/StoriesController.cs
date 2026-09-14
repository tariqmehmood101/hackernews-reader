using HackerNews.Api.Features.Story;
using HackerNews.Api.Features.Story.Dto;
using HackerNews.Api.Shared.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace HackerNews.Api.Controllers;

/// <summary>
/// Thin transport layer: binds the request, hands it to the mediator, unwraps the response.
/// No business logic lives here.
/// </summary>
[ApiController]
[Route("api/stories")]
[Produces("application/json")]
public sealed class StoriesController(ISender sender) : ControllerBase
{
    /// <summary>A page of the newest Hacker News stories, optionally filtered by a search term.</summary>
    [HttpGet("newest")]
    [ProducesResponseType(typeof(PagedResult<StoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<StoryDto>>> GetNewest(
        [FromQuery] GetNewestStoriesHandler.Query query,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(query, cancellationToken);
        return Ok(response.Result);
    }

    /// <summary>A single story by its Hacker News id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StoryDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await sender.Send(new GetStoryByIdHandler.Query(id), cancellationToken);
        return Ok(response.Story);
    }
}
