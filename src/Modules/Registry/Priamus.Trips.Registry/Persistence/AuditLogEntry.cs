namespace Priamus.Trips.Registry.Persistence;

/// <summary>
/// Stable string constants for the <c>event_type</c> column in the <c>registry.audit_log</c>
/// table. These strings are a compatibility contract: changing them is a breaking change
/// that requires a migration.
/// </summary>
public static class AuditEventTypes
{
    /// <summary>A new vehicle was created.</summary>
    public const string VehicleCreated = "registry.vehicle.created";

    /// <summary>A vehicle was edited.</summary>
    public const string VehicleUpdated = "registry.vehicle.updated";

    /// <summary>A vehicle was decommissioned.</summary>
    public const string VehicleDecommissioned = "registry.vehicle.decommissioned";

    /// <summary>A vehicle was reactivated.</summary>
    public const string VehicleReactivated = "registry.vehicle.reactivated";

    /// <summary>A new customer was created.</summary>
    public const string CustomerCreated = "registry.customer.created";

    /// <summary>A customer was edited.</summary>
    public const string CustomerUpdated = "registry.customer.updated";

    /// <summary>A customer was deactivated.</summary>
    public const string CustomerDeactivated = "registry.customer.deactivated";

    /// <summary>A customer was reactivated.</summary>
    public const string CustomerReactivated = "registry.customer.reactivated";

    /// <summary>A new driver was created.</summary>
    public const string DriverCreated = "registry.driver.created";

    /// <summary>A driver was edited.</summary>
    public const string DriverUpdated = "registry.driver.updated";

    /// <summary>A driver was deactivated.</summary>
    public const string DriverDeactivated = "registry.driver.deactivated";

    /// <summary>A driver was reactivated.</summary>
    public const string DriverReactivated = "registry.driver.reactivated";

    /// <summary>A new contract was created.</summary>
    public const string ContractCreated = "registry.contract.created";

    /// <summary>A contract was updated: its terms were edited, or it was closed on the day before a later contract starts (created in the same save).</summary>
    public const string ContractUpdated = "registry.contract.updated";

    /// <summary>The system night band was changed.</summary>
    public const string NightBandUpdated = "registry.night_band.updated";
}

/// <summary>
/// Persistence-layer entity written to the <c>registry.audit_log</c> table whenever a
/// domain event is raised by an aggregate of the module.
/// </summary>
/// <remarks>
/// This is a persistence-only type — it does NOT derive from <c>AggregateRoot</c> and
/// carries no domain events of its own. It is written inside the same EF Core transaction
/// as the originating aggregate state change via the <see cref="Interceptors.AuditInterceptor"/>.
/// </remarks>
public sealed class AuditLogEntry
{
    /// <summary>
    /// Initializes a new <see cref="AuditLogEntry"/>. All required fields must be supplied;
    /// there is no EF parameterless constructor because this entity is always created by the
    /// interceptor, never hydrated from the database through a navigation property.
    /// </summary>
    public AuditLogEntry(
        Guid id,
        DateTime occurredAt,
        Guid? actorId,
        Guid subjectId,
        string eventType,
        string payload)
    {
        Id = id;
        OccurredAt = occurredAt;
        ActorId = actorId;
        SubjectId = subjectId;
        EventType = eventType;
        Payload = payload;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// UTC instant at which the event occurred. Sourced from the domain event's own
    /// <c>OccurredAt</c> — not from a clock injected into the interceptor.
    /// </summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// Identifier of the authenticated actor at the time of the event, or <c>null</c>
    /// when the action is unauthenticated.
    /// </summary>
    public Guid? ActorId { get; private set; }

    /// <summary>
    /// Identifier of the affected entity. For vehicle events this is the <c>VehicleId</c>, for customer events the <c>CustomerId</c>, for driver events the <c>DriverId</c>, for contract events the <c>ContractId</c>, for night band events the id of the single setting row.
    /// </summary>
    public Guid SubjectId { get; private set; }

    /// <summary>
    /// Stable event-type string. See <see cref="AuditEventTypes"/> for the full list.
    /// </summary>
    public string EventType { get; private set; }

    /// <summary>Event data serialized as JSON (camelCase, enums by name, no type discriminator).</summary>
    public string Payload { get; private set; }
}
