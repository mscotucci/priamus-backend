namespace Priamus.Trips.SharedKernel.Domain;

/// <summary>
/// Marker interface for domain events raised by aggregates.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OccurredAt"/> is expected to be set by the aggregate at construction
/// time using its injected <see cref="Priamus.Trips.SharedKernel.Time.IClock"/>.
/// The aggregate MUST NOT call <see cref="DateTime.UtcNow"/> directly; the clock is
/// always passed in.
/// </para>
/// <para>
/// <see cref="EventId"/> is expected to be a fresh <see cref="Guid"/> per event
/// instance (typically <see cref="Guid.NewGuid()"/> in the record's primary constructor).
/// </para>
/// <para>
/// Domain events are published via the outbox pattern by the SaveChanges interceptor,
/// not by handlers directly. They are immutable <c>record</c> types named in past tense
/// (e.g. <c>TripRegistered</c>, <c>IssueOpened</c>).
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Unique identifier of this event instance.</summary>
    Guid EventId { get; }

    /// <summary>Wall-clock instant at which the event was raised, in UTC. Sourced from <c>IClock.UtcNow</c>.</summary>
    DateTime OccurredAt { get; }
}
