using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Contracts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "contracts",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                billing_level = table.Column<string>(type: "text", nullable: false),
                pricing_mode = table.Column<string>(type: "text", nullable: false),
                required_documents = table.Column<string[]>(type: "text[]", nullable: false),
                night_surcharge = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contracts", x => x.id);
                table.ForeignKey(
                    name: "FK_contracts_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "registry",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_contracts_customer_id",
            schema: "registry",
            table: "contracts",
            column: "customer_id");

        // A customer's validities never overlap (PRD §5.4). Deferred, so that closing the open
        // contract and inserting the next one in a single save are checked together at commit.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql(
            """
            ALTER TABLE registry.contracts
                ADD CONSTRAINT ex_contracts_customer_validity
                EXCLUDE USING gist (customer_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&)
                DEFERRABLE INITIALLY DEFERRED;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "contracts",
            schema: "registry");
    }
}
