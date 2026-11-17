using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Priamus.Trips.Identity.Domain.Users.Events;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="ISaveChangesInterceptor"/> that converts domain events buffered
/// on <see cref="AggregateRoot"/> instances into <see cref="AuditLogEntry"/> rows written
/// in the same transaction as the aggregate state change.
/// </summary>
/// <remarks>
/// This interceptor is registered as <b>scoped</b> in DI (because it depends on
/// <see cref="ICurrentUser"/> which is scoped) and added to the DbContext options via
/// <c>optionsBuilder.AddInterceptors(...)</c> in the DI registration extension.
///
/// The event → entry mapping is a switch expression (not a registry) because there are
/// exactly 7 event types in a single module; a generic registry adds indirection with no
/// benefit at this scale. See <c>docs/appendices/persistence.md</c> for the rationale.
///
/// <c>OccurredAt</c> is taken from the domain event, not from <see cref="IClock"/>.
/// The event timestamp is authoritative because it was stamped by the aggregate at the
/// moment of the state change, which may be slightly earlier than when EF flushes.
/// </remarks>
public sealed class AuditInterceptor : ISaveChangesInterceptor
{
    // Use the shared options from AuditSerializerOptions so that domain-event rows
    // and handler-written non-domain rows share an identical serialization format.
    private static readonly JsonSerializerOptions SerializerOptions = AuditSerializerOptions.Default;

    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditInterceptor"/>.
    /// </summary>
    /// <param name="currentUser">The current-user abstraction; provides <c>actor_id</c> for audit rows.</param>
    public AuditInterceptor(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return new ValueTask<InterceptionResult<int>>(result);
        }

        var context = eventData.Context;
        Guid? actorId = _currentUser.IsAuthenticated ? _currentUser.UserId.Value : null;

        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var entry = MapToAuditLogEntry(domainEvent, actorId);
                context.Set<AuditLogEntry>().Add(entry);
            }

            aggregate.ClearDomainEvents();
        }

        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is null)
        {
            return result;
        }

        var context = eventData.Context;
        Guid? actorId = _currentUser.IsAuthenticated ? _currentUser.UserId.Value : null;

        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var entry = MapToAuditLogEntry(domainEvent, actorId);
                context.Set<AuditLogEntry>().Add(entry);
            }

            aggregate.ClearDomainEvents();
        }

        return result;
    }

    /// <summary>
    /// Maps a domain event to an <see cref="AuditLogEntry"/> using a switch expression.
    /// The <c>subject_id</c> is always the affected <c>UserId</c>, carried on the event.
    /// </summary>
    private static AuditLogEntry MapToAuditLogEntry(IDomainEvent domainEvent, Guid? actorId)
    {
        return domainEvent switch
        {
            UserCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.UserId.Value,
                AuditEventTypes.UserCreated, e),

            UserPasswordChanged e => Create(
                e.EventId, e.OccurredAt, actorId, e.UserId.Value,
                AuditEventTypes.UserPasswordChanged, e),

            UserDisabled e => Create(
                e.EventId, e.OccurredAt, actorId, e.UserId.Value,
                AuditEventTypes.UserDisabled, e),

            UserEnabled e => Create(
                e.EventId, e.OccurredAt, actorId, e.UserId.Value,
                AuditEventTypes.UserEnabled, e),

            _ => throw new UnreachableException(
                $"Unknown domain event type '{domainEvent.GetType().FullName}' reached the AuditInterceptor. " +
                "Add a case to the switch expression in AuditInterceptor.MapToAuditLogEntry.")
        };
    }

    private static AuditLogEntry Create<TEvent>(
        Guid eventId,
        DateTime occurredAt,
        Guid? actorId,
        Guid subjectId,
        string eventType,
        TEvent payload)
        where TEvent : IDomainEvent
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new AuditLogEntry(eventId, occurredAt, actorId, subjectId, eventType, json);
    }
}
