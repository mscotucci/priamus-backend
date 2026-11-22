using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_ContractLocationRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "contract_location_rates",
            schema: "registry",
            columns: table => new
            {
                contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                pickup_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                delivery_location_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("PK_contract_location_rates", x => new { x.contract_id, x.id });
                table.ForeignKey(
                    name: "FK_contract_location_rates_contracts_contract_id",
                    column: x => x.contract_id,
                    principalSchema: "registry",
                    principalTable: "contracts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_contract_location_rates_locations_delivery_location_id",
                    column: x => x.delivery_location_id,
                    principalSchema: "registry",
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_contract_location_rates_locations_pickup_location_id",
                    column: x => x.pickup_location_id,
                    principalSchema: "registry",
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_contract_location_rates_delivery_location_id",
            schema: "registry",
            table: "contract_location_rates",
            column: "delivery_location_id");

        migrationBuilder.CreateIndex(
            name: "ix_contract_location_rates_pickup_location_id",
            schema: "registry",
            table: "contract_location_rates",
            column: "pickup_location_id");

        migrationBuilder.CreateIndex(
            name: "uq_contract_location_rates_locations",
            schema: "registry",
            table: "contract_location_rates",
            columns: new[] { "contract_id", "pickup_location_id", "delivery_location_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "contract_location_rates",
            schema: "registry");
    }
}
