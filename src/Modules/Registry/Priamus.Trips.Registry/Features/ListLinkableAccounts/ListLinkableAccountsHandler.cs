using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Features.ListDrivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListLinkableAccounts;

/// <summary>
/// Handles <c>GET /api/registry/drivers/linkable-accounts</c>: the enabled driver accounts not
/// linked to any driver, plus the account of the driver being edited, sorted by user name.
/// </summary>
internal sealed class ListLinkableAccountsHandler
{
    private readonly RegistryDbContext _db;
    private readonly IUserLookup _users;

    public ListLinkableAccountsHandler(RegistryDbContext db, IUserLookup users)
    {
        _db = db;
        _users = users;
    }

    /// <summary>
    /// Lists the accounts the secretariat can choose in the driver dialog.
    /// </summary>
    public async Task<Result<IReadOnlyList<DriverAccountDto>>> HandleAsync(
        ListLinkableAccountsRequest request,
        CancellationToken cancellationToken)
    {
        Guid? ownAccount = null;
        if (request.DriverId is { } driverId)
        {
            var id = new DriverId(driverId);
            var driver = await _db.Drivers
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new { d.UserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (driver is null)
            {
                return DriverErrors.NotFound;
            }

            ownAccount = driver.UserId?.Value;
        }

        var linked = (await _db.Drivers
                .AsNoTracking()
                .Where(d => d.UserId != null)
                .Select(d => d.UserId!.Value)
                .ToListAsync(cancellationToken))
            .Select(u => u.Value)
            .ToHashSet();

        var accounts = await _users.ListDriverAccountsAsync(cancellationToken);

        IReadOnlyList<DriverAccountDto> result = accounts
            .Where(a => a.Id == ownAccount || (!a.IsDisabled && !linked.Contains(a.Id)))
            .OrderBy(a => a.Username, StringComparer.Ordinal)
            .Select(a => new DriverAccountDto(a.Id, a.Username, a.IsDisabled))
            .ToList();

        return Result<IReadOnlyList<DriverAccountDto>>.Success(result);
    }
}
