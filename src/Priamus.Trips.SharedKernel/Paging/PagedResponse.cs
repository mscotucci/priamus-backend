namespace Priamus.Trips.SharedKernel.Paging;

/// <summary>
/// Response envelope for a paginated query. Immutable record.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Data">The items returned in the current page.</param>
/// <param name="TotalCount">Total number of items across all pages.</param>
/// <param name="Page">1-based page number returned.</param>
/// <param name="PageSize">Page size used to compute the result.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Data,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>Total number of pages, computed as <c>ceil(TotalCount / PageSize)</c>.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>True when there is a page after this one.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>True when there is a page before this one.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Builds an empty page envelope for the given page parameters.
    /// </summary>
    public static PagedResponse<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), 0, page, pageSize);
}
