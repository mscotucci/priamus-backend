namespace Priamus.Trips.SharedKernel.Domain;

/// <summary>
/// Base class for aggregate roots. Provides:
/// <list type="bullet">
///   <item>A domain-event buffer drained by the SaveChanges interceptor.</item>
///   <item>An optimistic-concurrency <see cref="Version"/> token managed by the aggregate.</item>
/// </list>
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Events raised by the aggregate since the last <see cref="ClearDomainEvents"/>.
    /// Drained by the persistence interceptor and forwarded to the outbox.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>
    /// Optimistic-concurrency token. EF Core maps this to <c>integer</c> on PostgreSQL.
    /// The setter is <c>protected</c> so EF Core can hydrate it and aggregates can
    /// increment it via <see cref="IncrementVersion"/>; consumers outside the aggregate
    /// cannot mutate it.
    /// </summary>
    public int Version { get; protected set; }

    /// <summary>
    /// Raises a domain event. Called from aggregate methods after applying a state change.
    /// </summary>
    /// <param name="domainEvent">The event to enqueue.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is null.</exception>
    protected void RaiseEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Clears the buffered domain events. Called by the persistence layer after the
    /// events have been persisted to the outbox.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Increments <see cref="Version"/> by 1. Called by aggregate methods after a
    /// state-changing operation to ensure subsequent updates can detect concurrent
    /// modifications.
    /// </summary>
    protected void IncrementVersion() => Version++;
}
