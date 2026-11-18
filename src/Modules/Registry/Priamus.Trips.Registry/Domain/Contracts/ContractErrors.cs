using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// Errors raised by the <see cref="Contract"/> aggregate, its validity and the application-layer
/// lookups on contracts.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Contract.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class ContractErrors
{
    /// <summary>The end of the validity is before its start (PRD §5.4).</summary>
    public static readonly Error ValidityEndBeforeStart = Error.Validation(
        "Registry.Contract.ValidityEndBeforeStart",
        "The validity end date cannot be before its start date.");

    /// <summary>The requested contract was not found for the customer.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Contract.NotFound",
        "The requested contract was not found.");

    /// <summary>
    /// The validity shares at least one day with another contract of the customer; the other
    /// contract's validity is passed as the message argument (PRD §5.4, RF-18).
    /// </summary>
    public static readonly Error Overlap = Error.Conflict(
        "Registry.Contract.Overlap",
        "The validity overlaps another contract of the customer.");

    /// <summary>
    /// The validity was taken by a save that won the race on the exclusion constraint, and the
    /// other contract can no longer be named.
    /// </summary>
    public static readonly Error OverlapWithAnother = Error.Conflict(
        "Registry.Contract.OverlapWithAnother",
        "The validity overlaps another contract of the customer.");

    /// <summary>A contract cannot be created for a deactivated customer.</summary>
    public static readonly Error CustomerDeactivated = Error.Conflict(
        "Registry.Contract.CustomerDeactivated",
        "The customer is deactivated: contracts cannot be added.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed the
    /// contract in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Contract.ConcurrentUpdate",
        "The contract was modified by another user in the meantime.");
}
