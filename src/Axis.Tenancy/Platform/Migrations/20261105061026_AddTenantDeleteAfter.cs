using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDeleteAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delete_after",
                schema: "platform",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create', 'export', 'delete', 'cancel-deletion')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "delete_after",
                schema: "platform",
                table: "tenants");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create', 'export')");
        }
    }
}
