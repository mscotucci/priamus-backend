using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence.Converters;

namespace Priamus.Trips.Identity.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="User"/> aggregate root.
/// Maps to the <c>identity.users</c> table.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity", t =>
        {
            t.HasCheckConstraint(
                "chk_users_role",
                "role IN ('Driver', 'BackOffice', 'Admin')");

            t.HasCheckConstraint(
                "chk_users_status",
                "status IN ('Active', 'Disabled')");
        });

        // Primary key
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasConversion(new UserIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        // Username — varchar(64), unique
        builder.Property(u => u.Username)
            .HasConversion(new UsernameConverter())
            .HasColumnName("username")
            .HasColumnType("varchar(64)")
            .IsRequired();

        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("uq_users_username");

        // PasswordHash — text (algorithm-agnostic)
        builder.Property(u => u.PasswordHash)
            .HasConversion(new PasswordHashConverter())
            .HasColumnName("password_hash")
            .HasColumnType("text")
            .IsRequired();

        // Role — stored by member name
        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasColumnName("role")
            .HasColumnType("text")
            .IsRequired();

        // Status — stored by member name
        builder.Property(u => u.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();

        // Audit timestamps — timestamptz
        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(u => u.CreatedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(u => u.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(u => u.LastModifiedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("last_modified_by")
            .HasColumnType("uuid")
            .IsRequired();

        // Optimistic concurrency — explicit int column (not xmin).
        // AggregateRoot.Version is incremented by the aggregate itself;
        // EF only uses it as a concurrency token.
        builder.Property<int>("Version")
            .HasColumnName("version")
            .HasColumnType("integer")
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // Index on role for authorization queries
        builder.HasIndex(u => u.Role)
            .HasDatabaseName("ix_users_role");
    }
}
