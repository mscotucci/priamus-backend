using Priamus.Trips.Registry.Domain.Vehicles.Events;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Vehicles;

/// <summary>
/// A vehicle of the company's own fleet, identified by its <see cref="ValueObjects.LicensePlate"/>
/// (PRD §5.2, §6.6, RF-26 to RF-30). Has exactly one <see cref="VehicleType"/> and one
/// <see cref="VehicleStatus"/>.
/// </summary>
public sealed class Vehicle : AggregateRoot
{
    /// <summary>Inclusive maximum length of the description once trimmed.</summary>
    public const int DescriptionMaxLength = 100;

    private Vehicle()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        LicensePlate = null!;
        Description = null!;
    }

    private Vehicle(
        VehicleId id,
        LicensePlate licensePlate,
        string description,
        VehicleType type,
        bool requiresTrailer,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        LicensePlate = licensePlate;
        Description = description;
        Type = type;
        RequiresTrailer = requiresTrailer;
        Status = VehicleStatus.Active;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public VehicleId Id { get; private set; }

    /// <summary>License plate in normal form.</summary>
    public LicensePlate LicensePlate { get; private set; }

    /// <summary>Free text that tells people which vehicle it is; trimmed, at most <see cref="DescriptionMaxLength"/> characters.</summary>
    public string Description { get; private set; }

    /// <summary>Role the vehicle can play in a trip.</summary>
    public VehicleType Type { get; private set; }

    /// <summary>Whether a trip led by this vehicle must name a trailer. Only ever true on a tractor.</summary>
    public bool RequiresTrailer { get; private set; }

    /// <summary>Whether the vehicle can be chosen for new trips.</summary>
    public VehicleStatus Status { get; private set; }

    /// <summary>UTC instant at which the vehicle was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor that created this vehicle.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the vehicle was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this vehicle.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>
    /// Creates an <see cref="VehicleStatus.Active"/> vehicle and stamps audit fields.
    /// </summary>
    /// <param name="id">Identifier of the new vehicle.</param>
    /// <param name="licensePlate">License plate in normal form.</param>
    /// <param name="description">Description; trimmed here.</param>
    /// <param name="type">Vehicle type.</param>
    /// <param name="requiresTrailer">Requires-trailer property; only allowed on a tractor.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="VehicleErrors.DescriptionInvalid"/> or
    /// <see cref="VehicleErrors.RequiresTrailerNotAllowed"/> when an invariant is violated.
    /// </exception>
    public static Vehicle Create(
        VehicleId id,
        LicensePlate licensePlate,
        string? description,
        VehicleType type,
        bool requiresTrailer,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(licensePlate);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmed = ValidateDescription(description);
        EnsureRequiresTrailerAllowed(type, requiresTrailer);

        var now = clock.UtcNow;
        var vehicle = new Vehicle(id, licensePlate, trimmed, type, requiresTrailer, now, createdBy);
        vehicle.RaiseEvent(new VehicleCreated(
            Guid.NewGuid(), now, id, licensePlate, trimmed, type, requiresTrailer, vehicle.Status));
        return vehicle;
    }

    /// <summary>
    /// Replaces the editable data of the vehicle. Always counts as a change.
    /// </summary>
    /// <param name="licensePlate">License plate in normal form.</param>
    /// <param name="description">Description; trimmed here.</param>
    /// <param name="type">Vehicle type.</param>
    /// <param name="requiresTrailer">Requires-trailer property; only allowed on a tractor.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="VehicleErrors.DescriptionInvalid"/> or
    /// <see cref="VehicleErrors.RequiresTrailerNotAllowed"/> when an invariant is violated.
    /// </exception>
    public void Update(
        LicensePlate licensePlate,
        string? description,
        VehicleType type,
        bool requiresTrailer,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(licensePlate);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmed = ValidateDescription(description);
        EnsureRequiresTrailerAllowed(type, requiresTrailer);

        LicensePlate = licensePlate;
        Description = trimmed;
        Type = type;
        RequiresTrailer = requiresTrailer;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new VehicleUpdated(
            Guid.NewGuid(), now, Id, licensePlate, trimmed, type, requiresTrailer, Status));
    }

    /// <summary>
    /// Decommissions the vehicle. A no-op on an already decommissioned vehicle: no event, no
    /// version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Decommission(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is VehicleStatus.Decommissioned)
        {
            return;
        }

        Status = VehicleStatus.Decommissioned;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new VehicleDecommissioned(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Reactivates the vehicle. A no-op on an already active vehicle: no event, no version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Reactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is VehicleStatus.Active)
        {
            return;
        }

        Status = VehicleStatus.Active;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new VehicleReactivated(Guid.NewGuid(), now, Id));
    }

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
    }

    private static string ValidateDescription(string? description)
    {
        var trimmed = description?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > DescriptionMaxLength)
        {
            throw new DomainException(VehicleErrors.DescriptionInvalid);
        }

        return trimmed;
    }

    /// <summary>
    /// Whether the requires-trailer property is allowed for the type: only a tractor can require
    /// a trailer (PRD §5.2, RF-27). Shared by the aggregate and the request validation.
    /// </summary>
    /// <param name="type">Vehicle type.</param>
    /// <param name="requiresTrailer">Requires-trailer property.</param>
    public static bool IsRequiresTrailerAllowed(VehicleType type, bool requiresTrailer) =>
        !requiresTrailer || type is VehicleType.Tractor;

    private static void EnsureRequiresTrailerAllowed(VehicleType type, bool requiresTrailer)
    {
        if (!IsRequiresTrailerAllowed(type, requiresTrailer))
        {
            throw new DomainException(VehicleErrors.RequiresTrailerNotAllowed);
        }
    }
}
