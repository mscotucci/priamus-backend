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
/// <see cref="Page"/> and <see cref="PageSize"/> are nullable so that
/// <c>[AsParameters]</c> binding treats them as optional. Readers should use the
/// derived <see cref="EffectivePage"/> and <see cref="EffectivePageSize"/>
/// properties, which resolve nulls to the documented defaults.
/// </para>
/// <para>
/// Sort columns are user-controlled string input; handlers MUST validate
/// <see cref="SortColumn"/> against a whitelist before using it to drive
/// <c>OrderBy</c>. Never pass it directly to <c>EF.Property</c> or <c>OrderBy(string)</c>.
/// </para>
/// </remarks>
public class PagedRequest
{
    /// <summary>Default page number when the client does not specify one.</summary>
    public const int DefaultPage = 1;

    /// <summary>Default page size when the client does not specify one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Maximum page size accepted; larger values are coerced to this.</summary>
    public const int MaxPageSize = 100;

    private int? _page;
    private int? _pageSize;

    /// <summary>
    /// 1-based page number. Optional on the wire; values below 1 are coerced to 1
    /// on the setter. Read via <see cref="EffectivePage"/> to get the resolved value
    /// (defaulting to <see cref="DefaultPage"/> when null).
    /// </summary>
    public int? Page
    {
        get => _page;
        set => _page = value switch
        {
            null => null,
            < 1 => DefaultPage,
            _ => value,
        };
    }

    /// <summary>
    /// Page size between 1 and <see cref="MaxPageSize"/>. Optional on the wire;
    /// non-positive values are coerced to <see cref="DefaultPageSize"/>, values
    /// greater than <see cref="MaxPageSize"/> are clamped to <see cref="MaxPageSize"/>.
    /// Read via <see cref="EffectivePageSize"/> to get the resolved value
    /// (defaulting to <see cref="DefaultPageSize"/> when null).
    /// </summary>
    public int? PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            null => null,
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

    /// <summary>
    /// Sort direction. Optional on the wire; readers should use
    /// <see cref="IsAscending"/> which resolves null to
    /// <see cref="SortDirection.Ascending"/>.
    /// </summary>
    public SortDirection? SortDirection { get; set; }

    /// <summary>
    /// The resolved page number used by handlers: <see cref="Page"/> when present,
    /// otherwise <see cref="DefaultPage"/>.
    /// </summary>
    public int EffectivePage => _page ?? DefaultPage;

    /// <summary>
    /// The resolved page size used by handlers: <see cref="PageSize"/> when present,
    /// otherwise <see cref="DefaultPageSize"/>.
    /// </summary>
    public int EffectivePageSize => _pageSize ?? DefaultPageSize;

    /// <summary>
    /// Number of items to skip, computed from <see cref="EffectivePage"/> and
    /// <see cref="EffectivePageSize"/>.
    /// </summary>
    public int Skip => (EffectivePage - 1) * EffectivePageSize;

    /// <summary>
    /// True when <see cref="SortDirection"/> is unspecified (treated as the default
    /// <see cref="SortDirection.Ascending"/>) or explicitly
    /// <see cref="SortDirection.Ascending"/>.
    /// </summary>
    public bool IsAscending => (SortDirection ?? Paging.SortDirection.Ascending) == Paging.SortDirection.Ascending;
}
