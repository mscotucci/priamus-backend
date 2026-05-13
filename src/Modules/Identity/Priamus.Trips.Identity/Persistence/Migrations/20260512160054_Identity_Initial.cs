using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Identity.Persistence.Migrations;

/// <inheritdoc />
public partial class Identity_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "audit_log",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_log", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.id);
                table.CheckConstraint("chk_refresh_tokens_rotation_implies_revocation", "replaced_by_token_id IS NULL OR revoked_at IS NOT NULL");
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username = table.Column<string>(type: "varchar(64)", nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                role = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
                table.CheckConstraint("chk_users_role", "role IN (1, 2, 3)");
                table.CheckConstraint("chk_users_status", "status IN (1, 2)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_event_type_occurred_at",
            schema: "identity",
            table: "audit_log",
            columns: new[] { "event_type", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_subject_id_occurred_at",
            schema: "identity",
            table: "audit_log",
            columns: new[] { "subject_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id",
            schema: "identity",
            table: "refresh_tokens",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id_expires_at_active",
            schema: "identity",
            table: "refresh_tokens",
            columns: new[] { "user_id", "expires_at" },
            filter: "revoked_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_refresh_tokens_token_hash",
            schema: "identity",
            table: "refresh_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_users_role",
            schema: "identity",
            table: "users",
            column: "role");

        migrationBuilder.CreateIndex(
            name: "uq_users_username",
            schema: "identity",
            table: "users",
            column: "username",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "refresh_tokens",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "users",
            schema: "identity");
    }
}
