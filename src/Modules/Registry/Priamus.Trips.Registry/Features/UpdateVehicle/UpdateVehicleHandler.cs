using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateVehicle;

/// <summary>
/// Handles <c>PUT /api/registry/vehicles/{id}</c>: replaces the editable data of a vehicle, active
/// or decommissioned (PRD §6.6, RF-26, RF-27, RF-27bis, RF-28).
/// </summary>
/// <remarks>
/// The version the client read becomes the original value of the concurrency token, so a vehicle
/// changed in the meantime fails the save with <see cref="DbUpdateConcurrencyException"/>, answered
/// as <see cref="VehicleErrors.ConcurrentUpdate"/>. The plate is checked upfront against the other
/// vehicles; a race on the unique index (<c>uq_vehicles_license_plate</c>) maps to the same errors.
/// </remarks>
internal sealed class UpdateVehicleHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _arguments;
    private readonly ILogger<UpdateVehicleHandler> _logger;

    public UpdateVehicleHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments arguments,
        ILogger<UpdateVehicleHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// Updates the vehicle identified by <paramref name="id"/> from the request.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        UpdateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        var vehicleId = new VehicleId(id);
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return VehicleErrors.NotFound;
        }

        if (request.Type is VehicleType.Trailer && vehicle.Type is not VehicleType.Trailer)
        {
            var customerName = await _db.Contracts
                .AsNoTracking()
                .Where(c => EF.Property<List<StoredVehicleRate>>(c, "_storedVehicleRates")
                    .Any(r => r.VehicleId == vehicleId))
                .Join(_db.Customers, c => c.CustomerId, cu => cu.Id, (c, cu) => cu.CompanyName)
                .OrderBy(name => name)
                .FirstOrDefaultAsync(cancellationToken);
            if (customerName is not null)
            {
                _arguments.Set(customerName);
                return VehicleErrors.HasVehicleRates;
            }
        }

        LicensePlate plate;
        try
        {
            plate = LicensePlate.Create(request.LicensePlate);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        var conflict = await LicensePlateConflicts.FindAsync(_db, plate, vehicleId, _arguments, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var actorId = _currentUser.UserId;

        try
        {
            vehicle.Update(
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

        _db.Entry(vehicle).Property(v => v.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VehicleErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (LicensePlateConflicts.IsUniqueViolation(ex))
        {
            return await LicensePlateConflicts.ErrorAfterLostRaceAsync(_db, plate, vehicleId, _arguments, cancellationToken);
        }

        _logger.LogInformation(
            "Vehicle {VehicleId} updated by actor {ActorId}", vehicle.Id.Value, actorId.Value);

        return Result.Success();
    }
}
