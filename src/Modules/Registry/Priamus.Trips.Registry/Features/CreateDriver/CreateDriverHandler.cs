using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Features.ListDrivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.CreateDriver;

/// <summary>
/// Handles <c>POST /api/registry/drivers</c>: registers a new, active driver, optionally linked to
/// an existing driver account (PRD §6.5, RF-24).
/// </summary>
/// <remarks>
/// Race-condition safety net: the account is checked upfront to tell an active holder from a
/// deactivated one, but if two concurrent requests slip through, the unique index
/// (<c>uq_drivers_user_id</c>) is caught on <see cref="DbUpdateException"/> and translated to the
/// same errors.
/// </remarks>
internal sealed class CreateDriverHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IUserLookup _users;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<CreateDriverHandler> _logger;

    public CreateDriverHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IUserLookup users,
        ErrorMessageArguments arguments,
        ILogger<CreateDriverHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _users = users;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Creates a driver from the request.
    /// </summary>
    public async Task<Result<DriverListItemDto>> HandleAsync(
        CreateDriverRequest request,
        CancellationToken cancellationToken)
    {
        var (account, accountError) = await DriverAccountChecks.CheckAsync(
            _users, _db, request.UserId, current: null, excluding: null, _arguments, cancellationToken);
        if (accountError is not null)
        {
            return accountError;
        }

        var actorId = _currentUser.UserId;

        Driver driver;
        try
        {
            driver = Driver.Create(
                DriverId.New(),
                request.FirstName,
                request.LastName,
                request.Phone,
                request.UserId is { } userId ? new UserId(userId) : null,
                _clock,
                actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        _db.Drivers.Add(driver);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (account is not null && DriverAccountConflicts.IsUniqueViolation(ex))
        {
            return await DriverAccountConflicts.ErrorAfterLostRaceAsync(
                _db, new UserId(account.Id), account.Username, excluding: null, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Driver {DriverId} created by actor {ActorId}", driver.Id.Value, actorId.Value);

        return new DriverListItemDto(
            Id: driver.Id.Value,
            FirstName: driver.FirstName,
            LastName: driver.LastName,
            Phone: driver.Phone,
            Account: account is null ? null : new DriverAccountDto(account.Id, account.Username, account.IsDisabled),
            Status: driver.Status,
            LastModifiedAt: driver.LastModifiedAt,
            Version: driver.Version);
    }
}
