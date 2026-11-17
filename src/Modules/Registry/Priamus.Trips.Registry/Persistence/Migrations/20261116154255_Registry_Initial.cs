using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "registry");

        migrationBuilder.CreateTable(
            name: "audit_log",
            schema: "registry",
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

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_event_type_occurred_at",
            schema: "registry",
            table: "audit_log",
            columns: new[] { "event_type", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_subject_id_occurred_at",
            schema: "registry",
            table: "audit_log",
            columns: new[] { "subject_id", "occurred_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log",
            schema: "registry");
    }
}
