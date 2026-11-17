using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Vehicles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "vehicles",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                license_plate = table.Column<string>(type: "varchar(10)", nullable: false),
                description = table.Column<string>(type: "varchar(100)", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                requires_trailer = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vehicles", x => x.id);
                table.CheckConstraint("chk_vehicles_requires_trailer_tractor", "requires_trailer = false OR type = 'Tractor'");
                table.CheckConstraint("chk_vehicles_status", "status IN ('Active', 'Decommissioned')");
                table.CheckConstraint("chk_vehicles_type", "type IN ('Tractor', 'Trailer', 'StandaloneVehicle')");
            });

        migrationBuilder.CreateIndex(
            name: "uq_vehicles_license_plate",
            schema: "registry",
            table: "vehicles",
            column: "license_plate",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "vehicles",
            schema: "registry");
    }
}
