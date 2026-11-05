using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddReadOnlyTenantStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_status",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_status",
                schema: "platform",
                table: "tenants",
                sql: "status IN ('provisioning', 'active', 'suspended', 'readOnly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'read-only', 'create', 'export', 'delete', 'cancel-deletion', 'purge')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_status",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_status",
                schema: "platform",
                table: "tenants",
                sql: "status IN ('provisioning', 'active', 'suspended')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create', 'export', 'delete', 'cancel-deletion', 'purge')");
        }
    }
}
