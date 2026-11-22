using System.Text.RegularExpressions;
using Priamus.Trips.Registry.Domain.Locations.Events;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Locations;

/// <summary>
/// A place a trip starts from or delivers to: a warehouse, a building site, a landfill, a plant
/// (PRD §5.1, RF-23). One registry for the whole company: a location belongs to no customer and has
/// no role, any location can be the pickup or the delivery location of a trip. Identified for people
/// by its name and municipality; has exactly one <see cref="LocationStatus"/>.
/// </summary>
public sealed partial class Location : AggregateRoot
{
    /// <summary>Inclusive maximum length of the name once trimmed.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Inclusive maximum length of the municipality once trimmed.</summary>
    public const int MunicipalityMaxLength = 100;

    /// <summary>Inclusive maximum length of the address once trimmed.</summary>
    public const int AddressMaxLength = 200;

    private Location()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        Name = null!;
        Municipality = null!;
        NameKey = null!;
        MunicipalityKey = null!;
    }

    private Location(
        LocationId id,
        string name,
        string municipality,
        string? address,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        Name = name;
        Municipality = municipality;
        NameKey = KeyOf(name);
        MunicipalityKey = KeyOf(municipality);
        Address = address;
        Status = LocationStatus.Active;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public LocationId Id { get; private set; }

    /// <summary>What people call the place; trimmed, at most <see cref="NameMaxLength"/> characters, inner spaces as typed.</summary>
    public string Name { get; private set; }

    /// <summary>The town the location is in; trimmed, at most <see cref="MunicipalityMaxLength"/> characters, inner spaces as typed.</summary>
    public string Municipality { get; private set; }

    /// <summary>One line of free text, trimmed, at most <see cref="AddressMaxLength"/> characters; <c>null</c> when not entered.</summary>
    public string? Address { get; private set; }

    /// <summary>Whether the location can be chosen for new rates and trips.</summary>
    public LocationStatus Status { get; private set; }

    /// <summary>
    /// <see cref="KeyOf"/> of <see cref="Name"/>; with <see cref="MunicipalityKey"/> it is unique among
    /// all locations. Never leaves the backend.
    /// </summary>
    public string NameKey { get; private set; }

    /// <summary>
    /// <see cref="KeyOf"/> of <see cref="Municipality"/>; with <see cref="NameKey"/> it is unique among
    /// all locations. Never leaves the backend.
    /// </summary>
    public string MunicipalityKey { get; private set; }

    /// <summary>UTC instant at which the location was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor that created this location.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the location was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this location.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>
    /// The form of a name or municipality used to tell whether two locations are the same: trimmed,
    /// every run of whitespace collapsed to one space, lowercase invariant (PRD §5.1).
    /// </summary>
    /// <param name="value">Name or municipality, as typed.</param>
    /// <returns>The comparison key of <paramref name="value"/>.</returns>
    public static string KeyOf(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WhitespaceRun().Replace(value.Trim(), " ").ToLowerInvariant();
    }

    /// <summary>
    /// Creates an <see cref="LocationStatus.Active"/> location and stamps audit fields.
    /// </summary>
    /// <param name="id">Identifier of the new location.</param>
    /// <param name="name">Name; trimmed here.</param>
    /// <param name="municipality">Municipality; trimmed here.</param>
    /// <param name="address">Address; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="LocationErrors.NameInvalid"/>, <see cref="LocationErrors.MunicipalityInvalid"/>
    /// or <see cref="LocationErrors.AddressInvalid"/> when an invariant is violated.
    /// </exception>
    public static Location Create(
        LocationId id,
        string? name,
        string? municipality,
        string? address,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = ValidateName(name);
        var trimmedMunicipality = ValidateMunicipality(municipality);
        var trimmedAddress = ValidateAddress(address);

        var now = clock.UtcNow;
        var location = new Location(id, trimmedName, trimmedMunicipality, trimmedAddress, now, createdBy);
        location.RaiseEvent(new LocationCreated(
            Guid.NewGuid(), now, id, trimmedName, trimmedMunicipality, trimmedAddress, location.Status));
        return location;
    }

    /// <summary>
    /// Replaces the editable data of the location. A no-op when nothing changes: no event, no version
    /// bump, so no audit row.
    /// </summary>
    /// <param name="name">Name; trimmed here.</param>
    /// <param name="municipality">Municipality; trimmed here.</param>
    /// <param name="address">Address; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="LocationErrors.NameInvalid"/>, <see cref="LocationErrors.MunicipalityInvalid"/>
    /// or <see cref="LocationErrors.AddressInvalid"/> when an invariant is violated.
    /// </exception>
    public void Update(
        string? name,
        string? municipality,
        string? address,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = ValidateName(name);
        var trimmedMunicipality = ValidateMunicipality(municipality);
        var trimmedAddress = ValidateAddress(address);

        if (Name == trimmedName && Municipality == trimmedMunicipality && Address == trimmedAddress)
        {
            return;
        }

        Name = trimmedName;
        Municipality = trimmedMunicipality;
        NameKey = KeyOf(trimmedName);
        MunicipalityKey = KeyOf(trimmedMunicipality);
        Address = trimmedAddress;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new LocationUpdated(
            Guid.NewGuid(), now, Id, trimmedName, trimmedMunicipality, trimmedAddress, Status));
    }

    /// <summary>
    /// Deactivates the location. A no-op on an already deactivated location: no event, no
    /// version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Deactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is LocationStatus.Deactivated)
        {
            return;
        }

        Status = LocationStatus.Deactivated;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new LocationDeactivated(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Reactivates the location. A no-op on an already active location: no event, no version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Reactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is LocationStatus.Active)
        {
            return;
        }

        Status = LocationStatus.Active;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new LocationReactivated(Guid.NewGuid(), now, Id));
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(LocationErrors.NameInvalid);
        }

        return trimmed;
    }

    private static string ValidateMunicipality(string? municipality)
    {
        var trimmed = municipality?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MunicipalityMaxLength)
        {
            throw new DomainException(LocationErrors.MunicipalityInvalid);
        }

        return trimmed;
    }

    private static string? ValidateAddress(string? address)
    {
        var trimmed = address?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > AddressMaxLength)
        {
            throw new DomainException(LocationErrors.AddressInvalid);
        }

        return trimmed;
    }
}
