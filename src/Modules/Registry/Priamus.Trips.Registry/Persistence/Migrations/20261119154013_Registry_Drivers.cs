using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Drivers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "drivers",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                first_name = table.Column<string>(type: "varchar(60)", nullable: false),
                last_name = table.Column<string>(type: "varchar(60)", nullable: false),
                phone = table.Column<string>(type: "varchar(20)", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_drivers", x => x.id);
                table.CheckConstraint("chk_drivers_status", "status IN ('Active', 'Deactivated')");
            });

        migrationBuilder.CreateIndex(
            name: "uq_drivers_user_id",
            schema: "registry",
            table: "drivers",
            column: "user_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "drivers",
            schema: "registry");
    }
}
