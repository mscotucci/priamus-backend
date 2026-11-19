using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_ContractRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "day_price",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "minimum_amount",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "minimum_overage_price",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "minimum_rule",
            schema: "registry",
            table: "contracts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "minimum_threshold",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "night_call_fee",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "night_price",
            schema: "registry",
            table: "contracts",
            type: "numeric(12,2)",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "day_price",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "minimum_amount",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "minimum_overage_price",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "minimum_rule",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "minimum_threshold",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "night_call_fee",
            schema: "registry",
            table: "contracts");

        migrationBuilder.DropColumn(
            name: "night_price",
            schema: "registry",
            table: "contracts");
    }
}
