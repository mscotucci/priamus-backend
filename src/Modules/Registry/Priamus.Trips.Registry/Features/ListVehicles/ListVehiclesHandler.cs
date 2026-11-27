using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListVehicles;

/// <summary>
/// Handles <c>GET /api/registry/vehicles</c>: returns a paginated, optionally searched and
/// filtered list of vehicles, by default ordered by plate A to Z.
/// </summary>
internal sealed class ListVehiclesHandler
{
    private readonly RegistryDbContext _db;
    private readonly ILogger<ListVehiclesHandler> _logger;

    private static readonly HashSet<string> AllowedSortColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "licensePlate",
            "description",
            "lastModifiedAt",
        };

    public ListVehiclesHandler(RegistryDbContext db, ILogger<ListVehiclesHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Executes the paginated vehicle query.
    /// </summary>
    public async Task<Result<PagedResponse<VehicleListItemDto>>> HandleAsync(
        ListVehiclesRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Vehicles.AsNoTracking();

        // ── Optional filters ─────────────────────────────────────────────────
        if (request.Type is { } type)
        {
            query = query.Where(v => v.Type == type);
        }

        if (request.Status is { } status)
        {
            query = query.Where(v => v.Status == status);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = VehicleSearch.Matching(query, search);
        }

        // ── Total count before paging ────────────────────────────────────────
        var totalCount = await query.CountAsync(cancellationToken);

        // ── Sort column resolution with whitelist fallback ───────────────────
        // An unknown column routes to the wildcard arm so the request's direction cannot
        // subvert the default order (plate ascending).
        var sortColumn = request.SortColumn?.Trim();
        var useDefault = string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn);
        sortColumn = useDefault ? null : sortColumn!.ToLowerInvariant();

        var ascending = request.IsAscending;

        var ordered = (sortColumn, ascending) switch
        {
            ("licenseplate",   true)  => query.OrderBy(v => v.LicensePlate),
            ("licenseplate",   false) => query.OrderByDescending(v => v.LicensePlate),
            ("description",    true)  => query.OrderBy(v => v.Description),
            ("description",    false) => query.OrderByDescending(v => v.Description),
            ("lastmodifiedat", true)  => query.OrderBy(v => v.LastModifiedAt),
            ("lastmodifiedat", false) => query.OrderByDescending(v => v.LastModifiedAt),
            _                         => query.OrderBy(v => v.LicensePlate), // default: plate A-Z
        };

        // Description and timestamps are not unique: the plate is, so ties get a
        // stable order and no vehicle appears on two pages or on none.
        query = ordered.ThenBy(v => v.LicensePlate);

        // ── Project, page, and materialise ───────────────────────────────────
        var effectivePage = request.EffectivePage;
        var effectivePageSize = request.EffectivePageSize;

        var items = await query
            .Skip(request.Skip)
            .Take(effectivePageSize)
            .Select(v => new VehicleListItemDto(
                v.Id.Value,
                v.LicensePlate.Value,
                v.Description,
                v.Type,
                v.RequiresTrailer,
                v.Status,
                v.LastModifiedAt,
                v.Version))
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Listed {Count} vehicles (page {Page}/{TotalPages}, filters: type={Type}, status={Status})",
            items.Count,
            effectivePage,
            (int)Math.Ceiling((double)totalCount / effectivePageSize),
            request.Type?.ToString() ?? "any",
            request.Status?.ToString() ?? "any");

        return new PagedResponse<VehicleListItemDto>(items, totalCount, effectivePage, effectivePageSize);
    }
}
