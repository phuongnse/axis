using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantHostRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_host_requests",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    host = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_host_requests", x => new { x.tenant_id, x.host, x.requested_at });
                    table.CheckConstraint("ck_tenant_host_requests_state", "state IN ('pending', 'verified', 'expired')");
                    table.ForeignKey(
                        name: "fk_tenant_host_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_host_requests_host",
                schema: "platform",
                table: "tenant_host_requests",
                column: "host");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_host_requests",
                schema: "platform");
        }
    }
}
