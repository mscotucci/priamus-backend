using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_ContractVehicleRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "contract_vehicle_rates",
            schema: "registry",
            columns: table => new
            {
                vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                day_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                night_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                night_call_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                minimum_rule = table.Column<string>(type: "text", nullable: false),
                minimum_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                minimum_threshold = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                minimum_overage_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contract_vehicle_rates", x => new { x.contract_id, x.vehicle_id });
                table.ForeignKey(
                    name: "FK_contract_vehicle_rates_contracts_contract_id",
                    column: x => x.contract_id,
                    principalSchema: "registry",
                    principalTable: "contracts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_contract_vehicle_rates_vehicles_vehicle_id",
                    column: x => x.vehicle_id,
                    principalSchema: "registry",
                    principalTable: "vehicles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_contract_vehicle_rates_vehicle_id",
            schema: "registry",
            table: "contract_vehicle_rates",
            column: "vehicle_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "contract_vehicle_rates",
            schema: "registry");
    }
}
