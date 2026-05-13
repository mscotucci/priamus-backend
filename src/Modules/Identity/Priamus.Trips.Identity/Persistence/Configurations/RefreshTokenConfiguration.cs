using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Persistence.Converters;

namespace Priamus.Trips.Identity.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="RefreshToken"/> aggregate root.
/// Maps to the <c>identity.refresh_tokens</c> table.
/// </summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", "identity", t =>
        {
            // A token is either active (revoked_at IS NULL) or revoked.
            // If replaced_by_token_id is set, revoked_at must also be set.
            t.HasCheckConstraint(
                "chk_refresh_tokens_rotation_implies_revocation",
                "replaced_by_token_id IS NULL OR revoked_at IS NOT NULL");
        });

        // Primary key
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .HasConversion(new RefreshTokenIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        // UserId — bare Guid reference to identity.users; no FK at DB level (same schema
        // but by convention we do not declare it as a DB FK since it is an aggregate boundary).
        builder.Property(rt => rt.UserId)
            .HasConversion(new UserIdConverter())
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        // TokenHash — varchar(64), unique
        builder.Property(rt => rt.TokenHash)
            .HasConversion(new RefreshTokenHashConverter())
            .HasColumnName("token_hash")
            .HasColumnType("varchar(64)")
            .IsRequired();

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_refresh_tokens_token_hash");

        // Timestamps
        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(rt => rt.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(rt => rt.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        // ReplacedByTokenId — nullable reference to the replacement token
        builder.Property(rt => rt.ReplacedByTokenId)
            .HasConversion(new RefreshTokenIdConverter())
            .HasColumnName("replaced_by_token_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        // Optimistic concurrency — explicit int column
        builder.Property<int>("Version")
            .HasColumnName("version")
            .HasColumnType("integer")
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // Index to support queries for a user's tokens (e.g. session list, revoke-all)
        builder.HasIndex(rt => rt.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        // Partial index for active-token lookups (the hot path in token validation)
        builder.HasIndex(rt => new { rt.UserId, rt.ExpiresAt })
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_tokens_user_id_expires_at_active");
    }
}
