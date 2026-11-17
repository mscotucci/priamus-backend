using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.CreateVehicle;

/// <summary>
/// Handles <c>POST /api/registry/vehicles</c>: registers a new, active vehicle
/// (PRD §6.6, RF-26, RF-27, RF-27bis, RF-28).
/// </summary>
/// <remarks>
/// Race-condition safety net: the plate is checked upfront to tell an active holder from a
/// decommissioned one, but if two concurrent requests slip through, the unique index
/// (<c>uq_vehicles_license_plate</c>) is caught on <see cref="DbUpdateException"/> and translated
/// to the same errors.
/// </remarks>
internal sealed class CreateVehicleHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<CreateVehicleHandler> _logger;

    public CreateVehicleHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<CreateVehicleHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Creates a vehicle from the request.
    /// </summary>
    public async Task<Result<CreateVehicleResponse>> HandleAsync(
        CreateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        LicensePlate plate;
        try
        {
            plate = LicensePlate.Create(request.LicensePlate);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        var conflict = await LicensePlateConflicts.FindAsync(_db, plate, excluding: null, _arguments, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var actorId = _currentUser.UserId;

        Vehicle vehicle;
        try
        {
            vehicle = Vehicle.Create(
                VehicleId.New(),
                plate,
                request.Description,
                request.Type!.Value,
                request.RequiresTrailer!.Value,
                _clock,
                actorId);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        _db.Vehicles.Add(vehicle);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (LicensePlateConflicts.IsUniqueViolation(ex))
        {
            return await LicensePlateConflicts.ErrorAfterLostRaceAsync(_db, plate, excluding: null, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Vehicle {VehicleId} created with plate {LicensePlate} by actor {ActorId}",
            vehicle.Id.Value, vehicle.LicensePlate.Value, actorId.Value);

        return new CreateVehicleResponse(
            Id: vehicle.Id.Value,
            LicensePlate: vehicle.LicensePlate.Value,
            Description: vehicle.Description,
            Type: vehicle.Type,
            RequiresTrailer: vehicle.RequiresTrailer,
            Status: vehicle.Status,
            LastModifiedAt: vehicle.LastModifiedAt,
            Version: vehicle.Version);
    }
}
