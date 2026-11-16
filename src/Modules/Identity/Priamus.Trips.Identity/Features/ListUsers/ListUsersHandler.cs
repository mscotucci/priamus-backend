using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Identity.Features.ListUsers;

/// <summary>
/// Handles <c>GET /api/identity/users</c>: returns a paginated, optionally filtered list of users.
/// </summary>
internal sealed class ListUsersHandler
{
    private readonly IdentityDbContext _db;
    private readonly ILogger<ListUsersHandler> _logger;

    private static readonly HashSet<string> AllowedSortColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "username",
            "role",
            "status",
            "createdAt",
            "lastModifiedAt",
        };

    public ListUsersHandler(IdentityDbContext db, ILogger<ListUsersHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Executes the paginated user query.
    /// </summary>
    public async Task<Result<PagedResponse<UserListItemDto>>> HandleAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Users.AsNoTracking();

        // ── Optional filters ─────────────────────────────────────────────────
        if (request.Role is { } role)
        {
            query = query.Where(u => u.Role == role);
        }

        if (request.Status is { } status)
        {
            query = query.Where(u => u.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.UsernameContains))
        {
            // The Username VO stores its value normalised to lowercase (see Username.Create),
            // so lowercasing the needle yields a case-insensitive substring match without
            // EF.Functions.ILike. The .Unwrap() extension is registered as a DbFunction
            // on the IdentityDbContext and is translated to the underlying varchar column
            // (see IdentityDbContext.OnModelCreating), letting Contains compose as a
            // standard SQL LIKE despite Username being mapped via a ValueConverter.
            var needle = request.UsernameContains.Trim().ToLowerInvariant();
            query = query.Where(u => u.Username.Unwrap().Contains(needle));
        }

        // ── Total count before paging ────────────────────────────────────────
        var totalCount = await query.CountAsync(cancellationToken);

        // ── Sort column resolution with whitelist fallback ───────────────────
        // Default sort is createdAt DESC. When the caller does not specify a column
        // (or specifies one outside the whitelist) we route to the wildcard arm so the
        // request's SortDirection cannot subvert the documented default ordering.
        var sortColumn = request.SortColumn?.Trim();
        var useDefault = string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn);
        sortColumn = useDefault ? null : sortColumn!.ToLowerInvariant();

        var ascending = request.IsAscending;

        // Sort on Username uses the value-object property directly: EF Core translates
        // the ORDER BY through the UsernameConverter into ORDER BY on the underlying
        // varchar column.
        var ordered = (sortColumn, ascending) switch
        {
            ("username",       true)  => query.OrderBy(u => u.Username),
            ("username",       false) => query.OrderByDescending(u => u.Username),
            ("role",           true)  => query.OrderBy(u => u.Role),
            ("role",           false) => query.OrderByDescending(u => u.Role),
            ("status",         true)  => query.OrderBy(u => u.Status),
            ("status",         false) => query.OrderByDescending(u => u.Status),
            ("createdat",      true)  => query.OrderBy(u => u.CreatedAt),
            ("createdat",      false) => query.OrderByDescending(u => u.CreatedAt),
            ("lastmodifiedat", true)  => query.OrderBy(u => u.LastModifiedAt),
            ("lastmodifiedat", false) => query.OrderByDescending(u => u.LastModifiedAt),
            _                         => query.OrderByDescending(u => u.CreatedAt), // default: createdAt DESC
        };

        // Role, status and timestamps are not unique: without a unique tie-breaker PostgreSQL
        // may return equal rows in a different order on each query, so a user could appear on
        // two pages or on none. The user name is unique and gives ties a readable A–Z order.
        query = ordered.ThenBy(u => u.Username);

        // ── Project, page, and materialise ───────────────────────────────────
        var effectivePage = request.EffectivePage;
        var effectivePageSize = request.EffectivePageSize;

        var items = await query
            .Skip(request.Skip)
            .Take(effectivePageSize)
            .Select(u => new UserListItemDto(
                u.Id.Value,
                u.Username.Value,
                u.Role,
                u.Status,
                u.CreatedAt,
                u.LastModifiedAt))
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Listed {Count} users (page {Page}/{TotalPages}, filters: role={Role}, status={Status})",
            items.Count,
            effectivePage,
            (int)Math.Ceiling((double)totalCount / effectivePageSize),
            request.Role?.ToString() ?? "any",
            request.Status?.ToString() ?? "any");

        return new PagedResponse<UserListItemDto>(items, totalCount, effectivePage, effectivePageSize);
    }
}
