using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Operations.Persistence.Migrations;

/// <inheritdoc />
public partial class Operations_Trips : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "operations");

        migrationBuilder.CreateSequence(
            name: "trip_number",
            schema: "operations");

        migrationBuilder.CreateTable(
            name: "trips",
            schema: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<long>(type: "bigint", nullable: true),
                driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                leading_vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                trailer_id = table.Column<Guid>(type: "uuid", nullable: true),
                pickup_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                delivery_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                added_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                added_on = table.Column<DateOnly>(type: "date", nullable: false),
                added_by = table.Column<Guid>(type: "uuid", nullable: false),
                day = table.Column<DateOnly>(type: "date", nullable: true),
                start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                end_time = table.Column<TimeOnly>(type: "time", nullable: true),
                ended_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                kilograms = table.Column<int>(type: "integer", nullable: true),
                kilometres = table.Column<int>(type: "integer", nullable: true),
                measure_kind = table.Column<string>(type: "text", nullable: true),
                required_documents = table.Column<string[]>(type: "text[]", nullable: false),
                deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                amount_after_minimum = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                applied_night_call_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                base_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                day_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                is_night = table.Column<bool>(type: "boolean", nullable: true),
                minimum_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                minimum_applied = table.Column<bool>(type: "boolean", nullable: true),
                minimum_kind = table.Column<string>(type: "text", nullable: true),
                minimum_overage_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                minimum_threshold = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                night_band_end = table.Column<TimeOnly>(type: "time", nullable: true),
                night_band_start = table.Column<TimeOnly>(type: "time", nullable: true),
                night_call_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                night_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                night_surcharge = table.Column<bool>(type: "boolean", nullable: true),
                quantity = table.Column<int>(type: "integer", nullable: true),
                rate_delivery_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                rate_pickup_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                rate_source = table.Column<string>(type: "text", nullable: true),
                rate_vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                total = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_trips", x => x.id);
                table.CheckConstraint("chk_trips_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL) AND (deleted_at IS NULL OR end_time IS NULL)");
                table.CheckConstraint("chk_trips_ended", "(end_time IS NULL AND ended_at IS NULL AND number IS NULL AND contract_id IS NULL AND measure_kind IS NULL AND rate_source IS NULL AND total IS NULL) OR (start_time IS NOT NULL AND end_time IS NOT NULL AND ended_at IS NOT NULL AND number IS NOT NULL AND contract_id IS NOT NULL AND measure_kind IS NOT NULL AND rate_source IS NOT NULL AND total IS NOT NULL)");
                table.CheckConstraint("chk_trips_measure_kind", "measure_kind IN ('StartAndEnd', 'Weight', 'Distance', 'StartOnly')");
                table.CheckConstraint("chk_trips_minimum_kind", "minimum_kind IN ('None', 'FixedMinimum', 'TieredOverage')");
                table.CheckConstraint("chk_trips_rate_source", "rate_source IN ('Contract', 'Vehicle', 'Location', 'LocationPair')");
                table.CheckConstraint("chk_trips_started", "(day IS NULL) = (start_time IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "uq_trips_driver_id_in_progress",
            schema: "operations",
            table: "trips",
            column: "driver_id",
            unique: true,
            filter: "start_time IS NOT NULL AND end_time IS NULL AND deleted_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_trips_number",
            schema: "operations",
            table: "trips",
            column: "number",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "trips",
            schema: "operations");

        migrationBuilder.DropSequence(
            name: "trip_number",
            schema: "operations");
    }
}
