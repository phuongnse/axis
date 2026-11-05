using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantFailedWorkCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "failed_instances",
                schema: "platform",
                table: "tenant_usage_latest",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "open_dead_letters",
                schema: "platform",
                table: "tenant_usage_latest",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "unknown_calls",
                schema: "platform",
                table: "tenant_usage_latest",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_usage_latest_failed_instances",
                schema: "platform",
                table: "tenant_usage_latest",
                sql: "failed_instances IS NULL OR failed_instances >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_usage_latest_open_dead_letters",
                schema: "platform",
                table: "tenant_usage_latest",
                sql: "open_dead_letters IS NULL OR open_dead_letters >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_usage_latest_unknown_calls",
                schema: "platform",
                table: "tenant_usage_latest",
                sql: "unknown_calls IS NULL OR unknown_calls >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_usage_latest_failed_instances",
                schema: "platform",
                table: "tenant_usage_latest");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_usage_latest_open_dead_letters",
                schema: "platform",
                table: "tenant_usage_latest");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_usage_latest_unknown_calls",
                schema: "platform",
                table: "tenant_usage_latest");

            migrationBuilder.DropColumn(
                name: "failed_instances",
                schema: "platform",
                table: "tenant_usage_latest");

            migrationBuilder.DropColumn(
                name: "open_dead_letters",
                schema: "platform",
                table: "tenant_usage_latest");

            migrationBuilder.DropColumn(
                name: "unknown_calls",
                schema: "platform",
                table: "tenant_usage_latest");
        }
    }
}
