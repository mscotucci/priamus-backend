using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.CreateLocation;

/// <summary>
/// Handles <c>POST /api/registry/locations</c>: registers a new, active location (PRD §5.1, RF-23).
/// </summary>
/// <remarks>
/// Race-condition safety net: the name and municipality are checked upfront to tell an active
/// holder from a deactivated one, but if two concurrent requests slip through, the unique index
/// (<c>uq_locations_name_municipality_key</c>) is caught on <see cref="DbUpdateException"/> and
/// translated to the same errors.
/// </remarks>
internal sealed class CreateLocationHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<CreateLocationHandler> _logger;

    public CreateLocationHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<CreateLocationHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Creates a location from the request.
    /// </summary>
    public async Task<Result<CreateLocationResponse>> HandleAsync(
        CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;

        Location location;
        try
        {
            location = Location.Create(
                LocationId.New(),
                request.Name,
                request.Municipality,
                request.Address,
                _clock,
                actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        var conflict = await LocationConflicts.FindAsync(
            _db, location.Name, location.Municipality, excluding: null, _arguments, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        _db.Locations.Add(location);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (LocationConflicts.IsUniqueViolation(ex))
        {
            return await LocationConflicts.ErrorAfterLostRaceAsync(
                _db, location.Name, location.Municipality, excluding: null, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Location {LocationId} created by actor {ActorId}", location.Id.Value, actorId.Value);

        return new CreateLocationResponse(
            Id: location.Id.Value,
            Name: location.Name,
            Municipality: location.Municipality,
            Address: location.Address,
            Status: location.Status,
            LastModifiedAt: location.LastModifiedAt,
            Version: location.Version);
    }
}
