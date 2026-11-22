using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListLocations;

/// <summary>
/// Handles <c>GET /api/registry/locations</c>: returns a paginated, optionally searched and
/// filtered list of locations, by default ordered by name, then municipality, A to Z.
/// </summary>
internal sealed class ListLocationsHandler
{
    private readonly RegistryDbContext _db;
    private readonly ILogger<ListLocationsHandler> _logger;

    private static readonly HashSet<string> AllowedSortColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "name",
            "municipality",
            "lastModifiedAt",
        };

    public ListLocationsHandler(RegistryDbContext db, ILogger<ListLocationsHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Executes the paginated location query.
    /// </summary>
    public async Task<Result<PagedResponse<LocationListItemDto>>> HandleAsync(
        ListLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Locations.AsNoTracking();

        // ── Optional filters ─────────────────────────────────────────────────
        if (request.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        // One needle, matched on the lowercased text of name, municipality and address (never the
        // collation). A search of spaces only leaves an empty needle, which would match every row,
        // so no filter is applied.
        var needle = request.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(needle))
        {
            query = query.Where(l => l.Name.ToLower().Contains(needle)
                || l.Municipality.ToLower().Contains(needle)
                || (l.Address != null && l.Address.ToLower().Contains(needle)));
        }

        // ── Total count before paging ────────────────────────────────────────
        var totalCount = await query.CountAsync(cancellationToken);

        // ── Sort column resolution with whitelist fallback ───────────────────
        // An unknown column routes to the wildcard arm so the request's direction cannot
        // subvert the default order (name, then municipality, ascending).
        var sortColumn = request.SortColumn?.Trim();
        var useDefault = string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn);
        sortColumn = useDefault ? null : sortColumn!.ToLowerInvariant();

        var ascending = request.IsAscending;

        // The id closes every key: names, municipalities and timestamps are not unique, the id is,
        // so ties get a stable order and no location appears on two pages or on none.
        var ordered = (sortColumn, ascending) switch
        {
            ("name",           true)  => query.OrderBy(l => l.Name).ThenBy(l => l.Municipality).ThenBy(l => l.Id),
            ("name",           false) => query.OrderByDescending(l => l.Name).ThenByDescending(l => l.Municipality).ThenByDescending(l => l.Id),
            ("municipality",   true)  => query.OrderBy(l => l.Municipality).ThenBy(l => l.Name).ThenBy(l => l.Id),
            ("municipality",   false) => query.OrderByDescending(l => l.Municipality).ThenByDescending(l => l.Name).ThenByDescending(l => l.Id),
            ("lastmodifiedat", true)  => query.OrderBy(l => l.LastModifiedAt).ThenBy(l => l.Id),
            ("lastmodifiedat", false) => query.OrderByDescending(l => l.LastModifiedAt).ThenByDescending(l => l.Id),
            _                         => query.OrderBy(l => l.Name).ThenBy(l => l.Municipality).ThenBy(l => l.Id), // default: name A-Z
        };

        // ── Project, page, and materialise ───────────────────────────────────
        var effectivePage = request.EffectivePage;
        var effectivePageSize = request.EffectivePageSize;

        var items = await ordered
            .Skip(request.Skip)
            .Take(effectivePageSize)
            .Select(LocationListItemDto.Projection)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Listed {Count} locations (page {Page}/{TotalPages}, filters: status={Status})",
            items.Count,
            effectivePage,
            (int)Math.Ceiling((double)totalCount / effectivePageSize),
            request.Status?.ToString() ?? "any");

        return new PagedResponse<LocationListItemDto>(items, totalCount, effectivePage, effectivePageSize);
    }
}
