namespace Priamus.Trips.Registry.Features.ListDrivers;

/// <summary>
/// The Identity user account a driver is linked to, read from Identity at the time of the request
/// (ADR 0013).
/// </summary>
/// <param name="UserId">Identifier of the user account.</param>
/// <param name="Username">Normalised login name of the account.</param>
/// <param name="IsDisabled">Whether the account is disabled.</param>
public sealed record DriverAccountDto(Guid UserId, string Username, bool IsDisabled);
