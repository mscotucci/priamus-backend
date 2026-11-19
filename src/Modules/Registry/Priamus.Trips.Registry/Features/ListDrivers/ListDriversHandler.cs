using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListDrivers;

/// <summary>
/// Handles <c>GET /api/registry/drivers</c>: returns a paginated, optionally searched and
/// filtered list of drivers, by default ordered by last name then first name A to Z, each with
/// its linked account read from Identity (ADR 0013).
/// </summary>
internal sealed class ListDriversHandler
{
    private readonly RegistryDbContext _db;
    private readonly IUserLookup _users;
    private readonly ILogger<ListDriversHandler> _logger;

    private static readonly HashSet<string> AllowedSortColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "name",
            "lastModifiedAt",
        };

    public ListDriversHandler(RegistryDbContext db, IUserLookup users, ILogger<ListDriversHandler> logger)
    {
        _db = db;
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// Executes the paginated driver query.
    /// </summary>
    public async Task<Result<PagedResponse<DriverListItemDto>>> HandleAsync(
        ListDriversRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Drivers.AsNoTracking();

        // ── Optional filters ─────────────────────────────────────────────────
        if (request.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Two needles: the text lowercased against both names (never the collation), and the
            // text without spaces against the phone number without spaces. A trimmed, non-empty
            // text keeps at least one character in both needles.
            var nameNeedle = search.ToLowerInvariant();
            var phoneNeedle = search.Replace(" ", string.Empty);

            query = query.Where(d => d.FirstName.ToLower().Contains(nameNeedle)
                || d.LastName.ToLower().Contains(nameNeedle)
                || (d.Phone != null && d.Phone.Replace(" ", "").Contains(phoneNeedle)));
        }

        // ── Total count before paging ────────────────────────────────────────
        var totalCount = await query.CountAsync(cancellationToken);

        // ── Sort column resolution with whitelist fallback ───────────────────
        // An unknown column routes to the wildcard arm so the request's direction cannot
        // subvert the default order (last name, first name ascending).
        var sortColumn = request.SortColumn?.Trim();
        var useDefault = string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn);
        sortColumn = useDefault ? null : sortColumn!.ToLowerInvariant();

        var ascending = request.IsAscending;

        // Names and timestamps are not unique: the id is, so ties get a stable order and no
        // driver appears on two pages or on none. A direction inverts the whole key.
        var ordered = (sortColumn, ascending) switch
        {
            ("lastmodifiedat", true)  => query.OrderBy(d => d.LastModifiedAt).ThenBy(d => d.Id),
            ("lastmodifiedat", false) => query.OrderByDescending(d => d.LastModifiedAt).ThenByDescending(d => d.Id),
            ("name",           false) => query.OrderByDescending(d => d.LastName).ThenByDescending(d => d.FirstName).ThenByDescending(d => d.Id),
            _                         => query.OrderBy(d => d.LastName).ThenBy(d => d.FirstName).ThenBy(d => d.Id), // default: name A-Z
        };

        // ── Page, and materialise ────────────────────────────────────────────
        var effectivePage = request.EffectivePage;
        var effectivePageSize = request.EffectivePageSize;

        var rows = await ordered
            .Skip(request.Skip)
            .Take(effectivePageSize)
            .Select(d => new
            {
                d.Id,
                d.FirstName,
                d.LastName,
                d.Phone,
                d.UserId,
                d.Status,
                d.LastModifiedAt,
                d.Version,
            })
            .ToListAsync(cancellationToken);

        // ── One Identity read for the whole page ─────────────────────────────
        var userIds = rows
            .Where(r => r.UserId is not null)
            .Select(r => r.UserId!.Value.Value)
            .Distinct()
            .ToList();
        var accounts = await _users.GetAccountsAsync(userIds, cancellationToken);

        var items = rows
            .Select(r => new DriverListItemDto(
                r.Id.Value,
                r.FirstName,
                r.LastName,
                r.Phone,
                r.UserId is { } linked && accounts.TryGetValue(linked.Value, out var account)
                    ? new DriverAccountDto(account.Id, account.Username, account.IsDisabled)
                    : null,
                r.Status,
                r.LastModifiedAt,
                r.Version))
            .ToList();

        _logger.LogInformation(
            "Listed {Count} drivers (page {Page}/{TotalPages}, filters: status={Status})",
            items.Count,
            effectivePage,
            (int)Math.Ceiling((double)totalCount / effectivePageSize),
            request.Status?.ToString() ?? "any");

        return new PagedResponse<DriverListItemDto>(items, totalCount, effectivePage, effectivePageSize);
    }
}
