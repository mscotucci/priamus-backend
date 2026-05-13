using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Priamus.Trips.Identity.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for <see cref="AuditLogEntry"/>.
/// Maps to the <c>identity.audit_log</c> table.
/// </summary>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log", "identity");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.ActorId)
            .HasColumnName("actor_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(e => e.SubjectId)
            .HasColumnName("subject_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        // Primary lookup: events for a given subject, newest first
        builder.HasIndex(e => new { e.SubjectId, e.OccurredAt })
            .HasDatabaseName("ix_audit_log_subject_id_occurred_at");

        // Secondary lookup: all events of a given type, newest first
        builder.HasIndex(e => new { e.EventType, e.OccurredAt })
            .HasDatabaseName("ix_audit_log_event_type_occurred_at");
    }
}
