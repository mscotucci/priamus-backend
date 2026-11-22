using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Locations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "locations",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "varchar(100)", nullable: false),
                municipality = table.Column<string>(type: "varchar(100)", nullable: false),
                address = table.Column<string>(type: "varchar(200)", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                name_key = table.Column<string>(type: "varchar(100)", nullable: false),
                municipality_key = table.Column<string>(type: "varchar(100)", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_locations", x => x.id);
                table.CheckConstraint("chk_locations_status", "status IN ('Active', 'Deactivated')");
            });

        migrationBuilder.CreateIndex(
            name: "uq_locations_name_municipality_key",
            schema: "registry",
            table: "locations",
            columns: new[] { "name_key", "municipality_key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "locations",
            schema: "registry");
    }
}
