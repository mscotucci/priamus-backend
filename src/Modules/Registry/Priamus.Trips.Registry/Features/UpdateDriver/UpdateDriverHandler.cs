using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateDriver;

/// <summary>
/// Handles <c>PUT /api/registry/drivers/{id}</c>: replaces the editable data of a driver, active or
/// deactivated, the linked account included (PRD §6.5, RF-24).
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one before anything else, so a stale
/// client always gets <see cref="DriverErrors.ConcurrentUpdate"/>, even when its account is taken.
/// It also becomes the original value of the concurrency token, so a driver changed between the
/// load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the same way.
/// The account is checked upfront against Identity and the other drivers; a race on the unique
/// index (<c>uq_drivers_user_id</c>) maps to the same errors.
/// </remarks>
internal sealed class UpdateDriverHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IUserLookup _users;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<UpdateDriverHandler> _logger;

    public UpdateDriverHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IUserLookup users,
        ErrorMessageArguments arguments,
        ILogger<UpdateDriverHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _users = users;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Updates the driver identified by <paramref name="id"/> from the request.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        UpdateDriverRequest request,
        CancellationToken cancellationToken)
    {
        var driverId = new DriverId(id);
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId, cancellationToken);
        if (driver is null)
        {
            return DriverErrors.NotFound;
        }

        if (driver.Version != request.Version!.Value)
        {
            return DriverErrors.ConcurrentUpdate;
        }

        var (account, accountError) = await DriverAccountChecks.CheckAsync(
            _users, _db, request.UserId, driver.UserId, driverId, _arguments, cancellationToken);
        if (accountError is not null)
        {
            return accountError;
        }

        var actorId = _currentUser.UserId;

        try
        {
            driver.Update(
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

        _db.Entry(driver).Property(d => d.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DriverErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (account is not null && DriverAccountConflicts.IsUniqueViolation(ex))
        {
            return await DriverAccountConflicts.ErrorAfterLostRaceAsync(
                _db, new UserId(account.Id), account.Username, driverId, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Driver {DriverId} updated by actor {ActorId}", driver.Id.Value, actorId.Value);

        return Result.Success();
    }
}
