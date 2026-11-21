using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.NightBands;

/// <summary>Errors raised when the system night band is changed.</summary>
/// <remarks>Codes follow the <c>Registry.{Area}.{ErrorName}</c> PascalCase convention.</remarks>
public static class NightBandErrors
{
    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed the
    /// night band in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.NightBand.ConcurrentUpdate",
        "The night band was modified by another user in the meantime.");
}
