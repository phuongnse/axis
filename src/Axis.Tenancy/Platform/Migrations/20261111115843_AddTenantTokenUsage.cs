using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_token_usage",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    binding = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    own_key = table.Column<bool>(type: "boolean", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_token_usage", x => new { x.tenant_id, x.day, x.binding, x.model, x.own_key });
                    table.CheckConstraint("ck_tenant_token_usage_input_tokens", "input_tokens >= 0");
                    table.CheckConstraint("ck_tenant_token_usage_output_tokens", "output_tokens >= 0");
                    table.ForeignKey(
                        name: "fk_tenant_token_usage_tenants_tenant_id",
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
                name: "tenant_token_usage",
                schema: "platform");
        }
    }
}
