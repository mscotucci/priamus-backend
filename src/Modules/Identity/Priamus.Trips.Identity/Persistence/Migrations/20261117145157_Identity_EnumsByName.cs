using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Priamus.Trips.Identity.Persistence.Migrations;

/// <inheritdoc />
public partial class Identity_EnumsByName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_users_role",
            schema: "identity",
            table: "users");

        migrationBuilder.DropCheckConstraint(
            name: "chk_users_status",
            schema: "identity",
            table: "users");

        migrationBuilder.Sql(
            """
            ALTER TABLE identity.users
                ALTER COLUMN status TYPE text
                USING CASE status
                    WHEN 1 THEN 'Active'
                    WHEN 2 THEN 'Disabled'
                END;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE identity.users
                ALTER COLUMN role TYPE text
                USING CASE role
                    WHEN 1 THEN 'Driver'
                    WHEN 2 THEN 'BackOffice'
                    WHEN 3 THEN 'Admin'
                END;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "chk_users_role",
            schema: "identity",
            table: "users",
            sql: "role IN ('Driver', 'BackOffice', 'Admin')");

        migrationBuilder.AddCheckConstraint(
            name: "chk_users_status",
            schema: "identity",
            table: "users",
            sql: "status IN ('Active', 'Disabled')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_users_role",
            schema: "identity",
            table: "users");

        migrationBuilder.DropCheckConstraint(
            name: "chk_users_status",
            schema: "identity",
            table: "users");

        migrationBuilder.Sql(
            """
            ALTER TABLE identity.users
                ALTER COLUMN status TYPE integer
                USING CASE status
                    WHEN 'Active' THEN 1
                    WHEN 'Disabled' THEN 2
                END;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE identity.users
                ALTER COLUMN role TYPE integer
                USING CASE role
                    WHEN 'Driver' THEN 1
                    WHEN 'BackOffice' THEN 2
                    WHEN 'Admin' THEN 3
                END;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "chk_users_role",
            schema: "identity",
            table: "users",
            sql: "role IN (1, 2, 3)");

        migrationBuilder.AddCheckConstraint(
            name: "chk_users_status",
            schema: "identity",
            table: "users",
            sql: "status IN (1, 2)");
    }
}
