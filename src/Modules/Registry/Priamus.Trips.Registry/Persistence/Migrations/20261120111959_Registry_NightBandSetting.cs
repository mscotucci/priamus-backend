using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_NightBandSetting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "night_band_settings",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_night_band_settings", x => x.id);
                table.CheckConstraint("chk_night_band_settings_single_row", "id = '00000000-0000-0000-0000-000000000001'");
            });

        // The single row, written outside the context so that it leaves no audit row.
        migrationBuilder.InsertData(
            table: "night_band_settings",
            schema: "registry",
            columns: ["id", "start_time", "end_time", "last_modified_at", "last_modified_by", "version"],
            values: new object[]
            {
                new Guid("00000000-0000-0000-0000-000000000001"),
                new TimeOnly(22, 0),
                new TimeOnly(6, 0),
                new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                0
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "night_band_settings",
            schema: "registry");
    }
}
