namespace HackerNews.Api.Features.Story.Dto;

/// <summary>
/// A Hacker News story as exposed by this API.
/// </summary>
/// <param name="Url">
/// Absolute link to the article. Genuinely optional &mdash; self posts such as "Ask HN"
/// carry no hyperlink, and clients must render those as plain text.
/// </param>
public sealed record StoryDto(
    int Id,
    string Title,
    string? Url,
    string? By,
    DateTimeOffset Time,
    int Score,
    int Descendants);
