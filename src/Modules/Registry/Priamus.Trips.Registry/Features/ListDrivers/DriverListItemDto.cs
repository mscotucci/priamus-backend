using Priamus.Trips.Registry.Domain.Drivers;

namespace Priamus.Trips.Registry.Features.ListDrivers;

/// <summary>
/// A driver as the API returns it (PRD §6.5, RF-24).
/// </summary>
/// <param name="Id">Identifier of the driver.</param>
/// <param name="FirstName">The trimmed first name.</param>
/// <param name="LastName">The trimmed last name.</param>
/// <param name="Phone">The phone number as typed, trimmed, or <c>null</c>.</param>
/// <param name="Account">The linked user account, or <c>null</c> when the driver has no access.</param>
/// <param name="Status">The status.</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this driver.</param>
public sealed record DriverListItemDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Phone,
    DriverAccountDto? Account,
    DriverStatus Status,
    DateTime LastModifiedAt,
    int Version);
