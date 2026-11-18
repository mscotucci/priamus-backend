using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Registry.Persistence.Migrations;

/// <inheritdoc />
public partial class Registry_Customers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_name = table.Column<string>(type: "varchar(150)", nullable: false),
                vat_number = table.Column<string>(type: "varchar(11)", nullable: true),
                tax_code = table.Column<string>(type: "varchar(16)", nullable: true),
                address = table.Column<string>(type: "varchar(200)", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                last_modified_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                last_modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.id);
                table.CheckConstraint("chk_customers_status", "status IN ('Active', 'Deactivated')");
            });

        migrationBuilder.CreateIndex(
            name: "uq_customers_vat_number",
            schema: "registry",
            table: "customers",
            column: "vat_number",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customers",
            schema: "registry");
    }
}
