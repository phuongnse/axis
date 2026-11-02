using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_usage",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    database_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    file_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    users = table.Column<int>(type: "integer", nullable: true),
                    applications = table.Column<int>(type: "integer", nullable: true),
                    emails_sent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    connector_calls = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    work_items_run = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_usage", x => new { x.tenant_id, x.day });
                    table.CheckConstraint("ck_tenant_usage_applications", "applications IS NULL OR applications >= 0");
                    table.CheckConstraint("ck_tenant_usage_connector_calls", "connector_calls >= 0");
                    table.CheckConstraint("ck_tenant_usage_database_bytes", "database_bytes >= 0");
                    table.CheckConstraint("ck_tenant_usage_emails_sent", "emails_sent >= 0");
                    table.CheckConstraint("ck_tenant_usage_file_bytes", "file_bytes >= 0");
                    table.CheckConstraint("ck_tenant_usage_users", "users IS NULL OR users >= 0");
                    table.CheckConstraint("ck_tenant_usage_work_items_run", "work_items_run >= 0");
                    table.ForeignKey(
                        name: "fk_tenant_usage_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_usage_latest",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    database_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sampled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_usage_latest", x => x.tenant_id);
                    table.CheckConstraint("ck_tenant_usage_latest_database_bytes", "database_bytes >= 0");
                    table.CheckConstraint("ck_tenant_usage_latest_file_bytes", "file_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_tenant_usage_latest_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_usage",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_usage_latest",
                schema: "platform");
        }
    }
}
