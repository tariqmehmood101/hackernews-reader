namespace HackerNews.Api.Shared.Models;

/// <summary>
/// One page of results plus the counts a client needs to render a pager. Feature-agnostic, which
/// is why it lives in Shared rather than under a feature.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    /// <summary>Applies skip/take to <paramref name="source"/> and captures the unpaged total.</summary>
    public static PagedResult<T> From(IReadOnlyList<T> source, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Widened to long on purpose. In int, (page - 1) * pageSize overflows once the product
        // passes ~2.1bn and wraps negative, and Enumerable.Skip silently treats a negative count
        // as zero — so a far-off page used to hand back page 1's items under its number. The cast
        // happens before the subtraction so that page = int.MinValue cannot underflow either.
        var skip = ((long)page - 1) * pageSize;

        IReadOnlyList<T> items = skip >= source.Count
            ? []
            : source.Skip((int)Math.Max(0, skip)).Take(pageSize).ToList();

        return new PagedResult<T>(items, source.Count, page, pageSize);
    }
}
