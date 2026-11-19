using System.Text.RegularExpressions;
using Priamus.Trips.Registry.Domain.Drivers.Events;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Drivers;

/// <summary>
/// A person who drives for the company (PRD §6.5, RF-24, RF-25). A registry record of its own,
/// optionally linked to the Identity user account the driver signs in with; has exactly one
/// <see cref="DriverStatus"/>.
/// </summary>
public sealed partial class Driver : AggregateRoot
{
    /// <summary>Inclusive maximum length of the first name once trimmed.</summary>
    public const int FirstNameMaxLength = 60;

    /// <summary>Inclusive maximum length of the last name once trimmed.</summary>
    public const int LastNameMaxLength = 60;

    /// <summary>Inclusive maximum length of the phone number once trimmed.</summary>
    public const int PhoneMaxLength = 20;

    private Driver()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        FirstName = null!;
        LastName = null!;
    }

    private Driver(
        DriverId id,
        string firstName,
        string lastName,
        string? phone,
        UserId? userId,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Phone = phone;
        UserId = userId;
        Status = DriverStatus.Active;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public DriverId Id { get; private set; }

    /// <summary>First name; trimmed, at most <see cref="FirstNameMaxLength"/> characters. Not unique.</summary>
    public string FirstName { get; private set; }

    /// <summary>Last name; trimmed, at most <see cref="LastNameMaxLength"/> characters. Not unique.</summary>
    public string LastName { get; private set; }

    /// <summary>Phone number as typed, trimmed, at most <see cref="PhoneMaxLength"/> characters; <c>null</c> when not entered.</summary>
    public string? Phone { get; private set; }

    /// <summary>Identifier of the linked Identity user account; <c>null</c> for a driver without access.</summary>
    public UserId? UserId { get; private set; }

    /// <summary>Whether the driver can be chosen for new trips.</summary>
    public DriverStatus Status { get; private set; }

    /// <summary>UTC instant at which the driver was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor that created this driver.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the driver was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this driver.</summary>
    public UserId LastModifiedBy { get; private set; }

    [GeneratedRegex(@"\A\+?[0-9 ]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    /// <summary>
    /// Whether <paramref name="phone"/>, once trimmed, is empty or a valid phone number: at most
    /// <see cref="PhoneMaxLength"/> characters of digits and spaces with an optional leading "+".
    /// Used by the request validator.
    /// </summary>
    /// <param name="phone">The raw text.</param>
    public static bool IsPhoneValid(string? phone)
    {
        return IsTrimmedPhoneValid(phone?.Trim());
    }

    private static bool IsTrimmedPhoneValid(string? trimmed)
    {
        return string.IsNullOrEmpty(trimmed)
            || (trimmed.Length <= PhoneMaxLength && PhoneRegex().IsMatch(trimmed));
    }

    /// <summary>
    /// Creates an <see cref="DriverStatus.Active"/> driver and stamps audit fields.
    /// </summary>
    /// <param name="id">Identifier of the new driver.</param>
    /// <param name="firstName">First name; trimmed here.</param>
    /// <param name="lastName">Last name; trimmed here.</param>
    /// <param name="phone">Phone number; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="userId">Linked user account, or <c>null</c>.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="DriverErrors.FirstNameInvalid"/>, <see cref="DriverErrors.LastNameInvalid"/>
    /// or <see cref="DriverErrors.PhoneInvalid"/> when an invariant is violated.
    /// </exception>
    public static Driver Create(
        DriverId id,
        string? firstName,
        string? lastName,
        string? phone,
        UserId? userId,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedFirstName = ValidateName(firstName, FirstNameMaxLength, DriverErrors.FirstNameInvalid);
        var trimmedLastName = ValidateName(lastName, LastNameMaxLength, DriverErrors.LastNameInvalid);
        var trimmedPhone = ValidatePhone(phone);

        var now = clock.UtcNow;
        var driver = new Driver(id, trimmedFirstName, trimmedLastName, trimmedPhone, userId, now, createdBy);
        driver.RaiseEvent(new DriverCreated(
            Guid.NewGuid(), now, id, trimmedFirstName, trimmedLastName, trimmedPhone, userId, driver.Status));
        return driver;
    }

    /// <summary>
    /// Replaces the editable data of the driver. Always counts as a change.
    /// </summary>
    /// <param name="firstName">First name; trimmed here.</param>
    /// <param name="lastName">Last name; trimmed here.</param>
    /// <param name="phone">Phone number; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="userId">Linked user account, or <c>null</c> to remove the link.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="DriverErrors.FirstNameInvalid"/>, <see cref="DriverErrors.LastNameInvalid"/>
    /// or <see cref="DriverErrors.PhoneInvalid"/> when an invariant is violated.
    /// </exception>
    public void Update(
        string? firstName,
        string? lastName,
        string? phone,
        UserId? userId,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedFirstName = ValidateName(firstName, FirstNameMaxLength, DriverErrors.FirstNameInvalid);
        var trimmedLastName = ValidateName(lastName, LastNameMaxLength, DriverErrors.LastNameInvalid);
        var trimmedPhone = ValidatePhone(phone);

        FirstName = trimmedFirstName;
        LastName = trimmedLastName;
        Phone = trimmedPhone;
        UserId = userId;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new DriverUpdated(
            Guid.NewGuid(), now, Id, trimmedFirstName, trimmedLastName, trimmedPhone, userId, Status));
    }

    /// <summary>
    /// Deactivates the driver. A no-op on an already deactivated driver: no event, no version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Deactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is DriverStatus.Deactivated)
        {
            return;
        }

        Status = DriverStatus.Deactivated;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new DriverDeactivated(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Reactivates the driver. A no-op on an already active driver: no event, no version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Reactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is DriverStatus.Active)
        {
            return;
        }

        Status = DriverStatus.Active;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new DriverReactivated(Guid.NewGuid(), now, Id));
    }

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
    }

    private static string ValidateName(string? name, int maxLength, Error error)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            throw new DomainException(error);
        }

        return trimmed;
    }

    private static string? ValidatePhone(string? phone)
    {
        var trimmed = phone?.Trim();

        if (!IsTrimmedPhoneValid(trimmed))
        {
            throw new DomainException(DriverErrors.PhoneInvalid);
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
