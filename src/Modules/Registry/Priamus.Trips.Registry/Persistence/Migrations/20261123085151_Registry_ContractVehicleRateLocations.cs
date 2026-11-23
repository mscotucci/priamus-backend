using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_ContractVehicleRateLocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_contract_vehicle_rates",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.AddColumn<int>(
            name: "id",
            schema: "registry",
            table: "contract_vehicle_rates",
            type: "integer",
            nullable: false,
            defaultValue: 0)
            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        migrationBuilder.AddColumn<Guid>(
            name: "delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddPrimaryKey(
            name: "PK_contract_vehicle_rates",
            schema: "registry",
            table: "contract_vehicle_rates",
            columns: new[] { "contract_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_contract_vehicle_rates_delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            column: "delivery_location_id");

        migrationBuilder.CreateIndex(
            name: "ix_contract_vehicle_rates_pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            column: "pickup_location_id");

        migrationBuilder.CreateIndex(
            name: "uq_contract_vehicle_rates_vehicle_locations",
            schema: "registry",
            table: "contract_vehicle_rates",
            columns: new[] { "contract_id", "vehicle_id", "pickup_location_id", "delivery_location_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.AddForeignKey(
            name: "FK_contract_vehicle_rates_locations_delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            column: "delivery_location_id",
            principalSchema: "registry",
            principalTable: "locations",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_contract_vehicle_rates_locations_pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            column: "pickup_location_id",
            principalSchema: "registry",
            principalTable: "locations",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_contract_vehicle_rates_locations_delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropForeignKey(
            name: "FK_contract_vehicle_rates_locations_pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropPrimaryKey(
            name: "PK_contract_vehicle_rates",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropIndex(
            name: "ix_contract_vehicle_rates_delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropIndex(
            name: "ix_contract_vehicle_rates_pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropIndex(
            name: "uq_contract_vehicle_rates_vehicle_locations",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropColumn(
            name: "id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropColumn(
            name: "delivery_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.DropColumn(
            name: "pickup_location_id",
            schema: "registry",
            table: "contract_vehicle_rates");

        migrationBuilder.AddPrimaryKey(
            name: "PK_contract_vehicle_rates",
            schema: "registry",
            table: "contract_vehicle_rates",
            columns: new[] { "contract_id", "vehicle_id" });
    }
}
