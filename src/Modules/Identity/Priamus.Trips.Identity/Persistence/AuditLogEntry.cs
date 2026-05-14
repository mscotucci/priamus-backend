namespace Priamus.Trips.Identity.Persistence;

/// <summary>
/// Stable string constants for the <c>event_type</c> column in the <c>identity.audit_log</c>
/// table. These strings are a compatibility contract: changing them is a breaking change
/// that requires a migration.
/// </summary>
public static class AuditEventTypes
{
    /// <summary>A new user was created.</summary>
    public const string UserCreated = "identity.user.created";

    /// <summary>A user's password hash was rotated.</summary>
    public const string UserPasswordChanged = "identity.user.password_changed";

    /// <summary>A user was disabled.</summary>
    public const string UserDisabled = "identity.user.disabled";

    /// <summary>A user was re-enabled.</summary>
    public const string UserEnabled = "identity.user.enabled";

    /// <summary>A refresh token was issued.</summary>
    public const string RefreshTokenIssued = "identity.refresh_token.issued";

    /// <summary>A refresh token was rotated (old revoked, new issued).</summary>
    public const string RefreshTokenRotated = "identity.refresh_token.rotated";

    /// <summary>A refresh token was explicitly revoked.</summary>
    public const string RefreshTokenRevoked = "identity.refresh_token.revoked";

    // ─── Handler-written non-domain intent events ─────────────────────────

    /// <summary>A user successfully authenticated at login.</summary>
    public const string UserLoggedIn = "identity.user.logged_in";

    /// <summary>A login attempt failed (known user, bad password or disabled account).</summary>
    public const string LoginFailed = "identity.user.login_failed";

    /// <summary>A refresh token was revoked by the token owner (self-service logout).</summary>
    public const string TokenRevokedByOwner = "identity.refresh_token.revoked_by_owner";

    /// <summary>A refresh token was revoked by a back-office user or admin.</summary>
    public const string TokenRevokedByAdmin = "identity.refresh_token.revoked_by_admin";
}

/// <summary>
/// Persistence-layer entity written to the <c>identity.audit_log</c> table whenever a
/// domain event is raised by a <c>User</c> or <c>RefreshToken</c> aggregate.
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
    /// when the action is unauthenticated (e.g. a system-issued token or failed login).
    /// </summary>
    public Guid? ActorId { get; private set; }

    /// <summary>
    /// Identifier of the affected user. For all Identity events this is the <c>UserId</c>
    /// carried by the event, not the <c>RefreshTokenId</c>.
    /// </summary>
    public Guid SubjectId { get; private set; }

    /// <summary>
    /// Stable event-type string. See <see cref="AuditEventTypes"/> for the full list.
    /// </summary>
    public string EventType { get; private set; }

    /// <summary>Full event serialized as JSON (camelCase, no type discriminator).</summary>
    public string Payload { get; private set; }

    /// <summary>
    /// Convenience factory for non-domain intent events written directly by handlers
    /// (e.g. <c>UserLoggedIn</c>, <c>LoginFailed</c>, <c>TokenRevokedByOwner</c>).
    /// Serializes <paramref name="payload"/> using <see cref="AuditSerializerOptions.Default"/>.
    /// </summary>
    /// <typeparam name="TPayload">Anonymous or named type carrying the audit payload fields.</typeparam>
    /// <param name="eventType">Stable event-type constant from <see cref="AuditEventTypes"/>.</param>
    /// <param name="occurredAt">UTC instant of the event.</param>
    /// <param name="actorId">The actor performing the action, or <c>null</c> for unauthenticated operations.</param>
    /// <param name="subjectId">The affected user's identifier.</param>
    /// <param name="payload">The payload object to serialize.</param>
    public static AuditLogEntry ForNonDomainEvent<TPayload>(
        string eventType,
        DateTime occurredAt,
        Guid? actorId,
        Guid subjectId,
        TPayload payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload, AuditSerializerOptions.Default);
        return new AuditLogEntry(Guid.NewGuid(), occurredAt, actorId, subjectId, eventType, json);
    }
}
