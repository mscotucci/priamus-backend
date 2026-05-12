namespace Priamus.Trips.SharedKernel.Paging;

/// <summary>
/// Base type for paginated query requests. Designed as a mutable <c>class</c>
/// (not a record) because ASP.NET Core query-string model binding populates
/// public setters. Setters coerce invalid input to safe defaults rather than throwing.
/// </summary>
/// <remarks>
/// Subclasses extend this type with slice-specific filters and are bound in
/// endpoints with <c>[AsParameters]</c>.
/// <para>
/// Sort columns are user-controlled string input; handlers MUST validate
/// <see cref="SortColumn"/> against a whitelist before using it to drive
/// <c>OrderBy</c>. Never pass it directly to <c>EF.Property</c> or <c>OrderBy(string)</c>.
/// </para>
/// </remarks>
public class PagedRequest
{
    /// <summary>Default page size when the client does not specify one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Maximum page size accepted; larger values are coerced to this.</summary>
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>
    /// 1-based page number. Values below 1 are coerced to 1.
    /// </summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Page size, between 1 and <see cref="MaxPageSize"/>. Invalid values are coerced
    /// to <see cref="DefaultPageSize"/> when non-positive, or to <see cref="MaxPageSize"/>
    /// when greater than that.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>
    /// Name of the column to sort by. Optional; handlers fall back to a default
    /// safe column when this is null or not in their whitelist.
    /// </summary>
    public string? SortColumn { get; set; }

    /// <summary>Sort direction. Defaults to ascending.</summary>
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    /// <summary>Number of items to skip, computed from <see cref="Page"/> and <see cref="PageSize"/>.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>True when <see cref="SortDirection"/> is <see cref="SortDirection.Ascending"/>.</summary>
    public bool IsAscending => SortDirection == SortDirection.Ascending;
}
