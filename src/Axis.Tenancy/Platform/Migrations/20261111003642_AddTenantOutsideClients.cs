using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOutsideClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "ai_allowed_clients",
                schema: "platform",
                table: "tenants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "ai_outside_clients",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_ai_outside_clients",
                schema: "platform",
                table: "tenants",
                sql: "ai_outside_clients IN ('all', 'allowList', 'none')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_ai_outside_clients",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ai_allowed_clients",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ai_outside_clients",
                schema: "platform",
                table: "tenants");
        }
    }
}
