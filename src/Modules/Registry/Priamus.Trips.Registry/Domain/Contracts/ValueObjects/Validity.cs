using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The days a <see cref="Contract"/> applies to: a start day and, when known, an end day, both
/// included. A validity without an end is open and extends without end (PRD §5.4).
/// </summary>
public sealed record Validity
{
    private Validity(DateOnly validFrom, DateOnly? validTo)
    {
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    /// <summary>First day of the validity.</summary>
    public DateOnly ValidFrom { get; }

    /// <summary>Last day of the validity; <c>null</c> when the validity is open.</summary>
    public DateOnly? ValidTo { get; }

    /// <summary>Whether the validity has no end.</summary>
    public bool IsOpen => ValidTo is null;

    /// <summary>
    /// Builds a validity. The same day for start and end is valid.
    /// </summary>
    /// <param name="validFrom">First day.</param>
    /// <param name="validTo">Last day, or <c>null</c> for an open validity.</param>
    /// <returns>A failure with <see cref="ContractErrors.ValidityEndBeforeStart"/> when the end is before the start.</returns>
    public static Result<Validity> Create(DateOnly validFrom, DateOnly? validTo) =>
        validTo is { } end && end < validFrom
            ? ContractErrors.ValidityEndBeforeStart
            : new Validity(validFrom, validTo);

    /// <summary>Whether the two validities share at least one day.</summary>
    /// <param name="other">The other validity.</param>
    public bool Overlaps(Validity other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return (ValidTo is null || other.ValidFrom <= ValidTo)
            && (other.ValidTo is null || ValidFrom <= other.ValidTo);
    }

    /// <summary>Where <paramref name="today"/> falls against this validity.</summary>
    /// <param name="today">The day to compare with.</param>
    public ContractStatus StatusOn(DateOnly today)
    {
        if (today < ValidFrom)
        {
            return ContractStatus.Upcoming;
        }

        return ValidTo is { } end && end < today ? ContractStatus.Expired : ContractStatus.Current;
    }

    /// <summary>The same validity ending on <paramref name="lastDay"/>.</summary>
    /// <param name="lastDay">New last day.</param>
    /// <returns>A failure with <see cref="ContractErrors.ValidityEndBeforeStart"/> when it is before the start.</returns>
    public Result<Validity> CloseOn(DateOnly lastDay) => Create(ValidFrom, lastDay);
}
