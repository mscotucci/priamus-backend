using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Identity.Features.ListUsers;

/// <summary>
/// Handles <c>GET /api/users</c>: returns a paginated, optionally filtered list of users.
/// </summary>
public sealed class ListUsersHandler
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
            // Username.Value is already stored as lowercased (domain normalization invariant).
            // Lowercasing the needle ensures case-insensitive containment without EF.Functions.ILike.
            var needle = request.UsernameContains.Trim().ToLowerInvariant();
            query = query.Where(u => u.Username.Value.Contains(needle));
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

        query = (sortColumn, ascending) switch
        {
            ("username",       true)  => query.OrderBy(u => u.Username.Value),
            ("username",       false) => query.OrderByDescending(u => u.Username.Value),
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

        // ── Project, page, and materialise ───────────────────────────────────
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
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
            request.Page,
            (int)Math.Ceiling((double)totalCount / request.PageSize),
            request.Role?.ToString() ?? "any",
            request.Status?.ToString() ?? "any");

        return new PagedResponse<UserListItemDto>(items, totalCount, request.Page, request.PageSize);
    }
}
