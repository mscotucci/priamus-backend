using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateLocation;

/// <summary>
/// Handles <c>PUT /api/registry/locations/{id}</c>: replaces the editable data of a location, active
/// or deactivated (PRD §5.1, RF-23).
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one before anything else, so a stale
/// client always gets <see cref="LocationErrors.ConcurrentUpdate"/>, even when its name is taken. It
/// also becomes the original value of the concurrency token, so a location changed between the load
/// and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the same way. The
/// name and municipality are checked upfront against the other locations; a race on the unique index
/// (<c>uq_locations_name_municipality_key</c>) maps to the same errors.
/// </remarks>
internal sealed class UpdateLocationHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<UpdateLocationHandler> _logger;

    public UpdateLocationHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<UpdateLocationHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Updates the location identified by <paramref name="id"/> from the request.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var locationId = new LocationId(id);
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is null)
        {
            return LocationErrors.NotFound;
        }

        if (location.Version != request.Version!.Value)
        {
            return LocationErrors.ConcurrentUpdate;
        }

        var actorId = _currentUser.UserId;

        try
        {
            location.Update(request.Name, request.Municipality, request.Address, _clock, actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        var conflict = await LocationConflicts.FindAsync(
            _db, location.Name, location.Municipality, locationId, _arguments, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        _db.Entry(location).Property(l => l.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return LocationErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (LocationConflicts.IsUniqueViolation(ex))
        {
            return await LocationConflicts.ErrorAfterLostRaceAsync(
                _db, location.Name, location.Municipality, locationId, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Location {LocationId} updated by actor {ActorId}", location.Id.Value, actorId.Value);

        return Result.Success();
    }
}
