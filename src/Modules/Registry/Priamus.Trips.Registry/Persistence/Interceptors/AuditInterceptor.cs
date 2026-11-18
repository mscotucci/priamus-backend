using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.Events;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.Events;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="ISaveChangesInterceptor"/> that converts domain events buffered
/// on <see cref="AggregateRoot"/> instances into <see cref="AuditLogEntry"/> rows written
/// in the same transaction as the aggregate state change.
/// </summary>
/// <remarks>
/// Registered as <b>scoped</b> in DI (because it depends on <see cref="ICurrentUser"/>, which is
/// scoped) and added to the DbContext options in the DI registration extension. The event → entry
/// mapping is a switch expression: the module has eight event types. Created and updated rows carry
/// the full state after the change, with the plate, VAT number and tax code as plain strings and enums by name; the
/// payload is built explicitly instead of serializing the event, whose value objects would
/// otherwise appear as nested objects. <c>OccurredAt</c> is taken from the domain event, not
/// from a clock.
/// </remarks>
public sealed class AuditInterceptor : ISaveChangesInterceptor
{
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
        AddAuditRows(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditRows(eventData);
        return result;
    }

    private void AddAuditRows(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
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
                context.Set<AuditLogEntry>().Add(MapToAuditLogEntry(domainEvent, actorId));
            }

            aggregate.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Maps a domain event to an <see cref="AuditLogEntry"/>. The <c>subject_id</c> is always
    /// the affected <c>VehicleId</c> or <c>CustomerId</c>.
    /// </summary>
    internal static AuditLogEntry MapToAuditLogEntry(IDomainEvent domainEvent, Guid? actorId)
    {
        return domainEvent switch
        {
            VehicleCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleCreated,
                FullState(e.VehicleId.Value, e.LicensePlate.Value, e.Description, e.Type, e.RequiresTrailer, e.Status)),

            VehicleUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleUpdated,
                FullState(e.VehicleId.Value, e.LicensePlate.Value, e.Description, e.Type, e.RequiresTrailer, e.Status)),

            VehicleDecommissioned e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleDecommissioned,
                new { VehicleId = e.VehicleId.Value }),

            VehicleReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleReactivated,
                new { VehicleId = e.VehicleId.Value }),

            CustomerCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerCreated,
                CustomerState(e.CustomerId, e.CompanyName, e.VatNumber, e.TaxCode, e.Address, e.Status)),

            CustomerUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerUpdated,
                CustomerState(e.CustomerId, e.CompanyName, e.VatNumber, e.TaxCode, e.Address, e.Status)),

            CustomerDeactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerDeactivated,
                new { CustomerId = e.CustomerId.Value }),

            CustomerReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerReactivated,
                new { CustomerId = e.CustomerId.Value }),

            _ => throw new UnreachableException(
                $"Unknown domain event type '{domainEvent.GetType().FullName}' reached the AuditInterceptor. " +
                "Add a case to the switch expression in AuditInterceptor.MapToAuditLogEntry.")
        };
    }

    private static object FullState(
        Guid vehicleId,
        string licensePlate,
        string description,
        VehicleType type,
        bool requiresTrailer,
        VehicleStatus status) =>
        new
        {
            VehicleId = vehicleId,
            LicensePlate = licensePlate,
            Description = description,
            Type = type,
            RequiresTrailer = requiresTrailer,
            Status = status,
        };

    private static object CustomerState(
        CustomerId customerId,
        string companyName,
        VatNumber? vatNumber,
        TaxCode? taxCode,
        string? address,
        CustomerStatus status) =>
        new
        {
            CustomerId = customerId.Value,
            CompanyName = companyName,
            VatNumber = vatNumber?.Value,
            TaxCode = taxCode?.Value,
            Address = address,
            Status = status,
        };

    private static AuditLogEntry Create<TPayload>(
        Guid eventId,
        DateTime occurredAt,
        Guid? actorId,
        Guid subjectId,
        string eventType,
        TPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new AuditLogEntry(eventId, occurredAt, actorId, subjectId, eventType, json);
    }
}
