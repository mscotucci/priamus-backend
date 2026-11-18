namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// Where a day falls against the validity of a <see cref="Contract"/>; derived, never stored
/// (PRD §5.4).
/// </summary>
public enum ContractStatus
{
    /// <summary>In vigore: the day is inside the validity.</summary>
    Current,

    /// <summary>Futuro: the validity starts after the day.</summary>
    Upcoming,

    /// <summary>Scaduto: the validity ended before the day.</summary>
    Expired,
}
