using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListCustomers;

/// <summary>
/// Handles <c>GET /api/registry/customers</c>: returns a paginated, optionally searched and
/// filtered list of customers, by default ordered by company name A to Z.
/// </summary>
internal sealed class ListCustomersHandler
{
    private readonly RegistryDbContext _db;
    private readonly ILogger<ListCustomersHandler> _logger;

    private static readonly HashSet<string> AllowedSortColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "companyName",
            "lastModifiedAt",
        };

    public ListCustomersHandler(RegistryDbContext db, ILogger<ListCustomersHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Executes the paginated customer query.
    /// </summary>
    public async Task<Result<PagedResponse<CustomerListItemDto>>> HandleAsync(
        ListCustomersRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Customers.AsNoTracking();

        // ── Optional filters ─────────────────────────────────────────────────
        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Three needles: the company name on its lowercased text (never the collation), the
            // VAT number and the tax code normalised like the stored values. A needle left empty
            // by normalisation (a bare "IT", for the VAT number) would match every row, so it is
            // skipped; the company-name needle is never empty here.
            var nameNeedle = search.ToLowerInvariant();
            var vatNeedle = VatNumber.Normalize(search);
            var taxNeedle = TaxCode.Normalize(search);

            query = (vatNeedle.Length == 0, taxNeedle.Length == 0) switch
            {
                (true, true) => query.Where(c => c.CompanyName.ToLower().Contains(nameNeedle)),
                (false, true) => query.Where(c => c.CompanyName.ToLower().Contains(nameNeedle)
                    || c.VatNumber.Unwrap().Contains(vatNeedle)),
                (true, false) => query.Where(c => c.CompanyName.ToLower().Contains(nameNeedle)
                    || c.TaxCode.Unwrap().Contains(taxNeedle)),
                (false, false) => query.Where(c => c.CompanyName.ToLower().Contains(nameNeedle)
                    || c.VatNumber.Unwrap().Contains(vatNeedle)
                    || c.TaxCode.Unwrap().Contains(taxNeedle)),
            };
        }

        // ── Total count before paging ────────────────────────────────────────
        var totalCount = await query.CountAsync(cancellationToken);

        // ── Sort column resolution with whitelist fallback ───────────────────
        // An unknown column routes to the wildcard arm so the request's direction cannot
        // subvert the default order (company name ascending).
        var sortColumn = request.SortColumn?.Trim();
        var useDefault = string.IsNullOrEmpty(sortColumn) || !AllowedSortColumns.Contains(sortColumn);
        sortColumn = useDefault ? null : sortColumn!.ToLowerInvariant();

        var ascending = request.IsAscending;

        var ordered = (sortColumn, ascending) switch
        {
            ("companyname",    true)  => query.OrderBy(c => c.CompanyName),
            ("companyname",    false) => query.OrderByDescending(c => c.CompanyName),
            ("lastmodifiedat", true)  => query.OrderBy(c => c.LastModifiedAt),
            ("lastmodifiedat", false) => query.OrderByDescending(c => c.LastModifiedAt),
            _                         => query.OrderBy(c => c.CompanyName), // default: name A-Z
        };

        // Company names and timestamps are not unique: the id is, so ties get a stable order
        // and no customer appears on two pages or on none.
        var stable = ordered.ThenBy(c => c.Id);

        // ── Project, page, and materialise ───────────────────────────────────
        var effectivePage = request.EffectivePage;
        var effectivePageSize = request.EffectivePageSize;

        var items = await stable
            .Skip(request.Skip)
            .Take(effectivePageSize)
            .Select(c => new CustomerListItemDto(
                c.Id.Value,
                c.CompanyName,
                c.VatNumber == null ? null : c.VatNumber.Value,
                c.TaxCode == null ? null : c.TaxCode.Value,
                c.Address,
                c.Status,
                c.LastModifiedAt,
                c.Version))
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Listed {Count} customers (page {Page}/{TotalPages}, filters: status={Status})",
            items.Count,
            effectivePage,
            (int)Math.Ceiling((double)totalCount / effectivePageSize),
            request.Status?.ToString() ?? "any");

        return new PagedResponse<CustomerListItemDto>(items, totalCount, effectivePage, effectivePageSize);
    }
}
