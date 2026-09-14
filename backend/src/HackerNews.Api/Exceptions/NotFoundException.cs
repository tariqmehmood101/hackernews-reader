namespace HackerNews.Api.Exceptions;

/// <summary>Requested resource does not exist. Surfaces as HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);
