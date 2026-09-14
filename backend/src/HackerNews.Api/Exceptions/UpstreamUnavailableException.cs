namespace HackerNews.Api.Exceptions;

/// <summary>
/// The Hacker News API could not be reached and we have no cached data to fall back on.
/// Surfaces as HTTP 503 &mdash; deliberately not an empty 200, which would look like "no stories".
/// </summary>
public sealed class UpstreamUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
