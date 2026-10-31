using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDataKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_data_keys",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_data_keys", x => new { x.tenant_id, x.kind, x.number });
                    table.CheckConstraint("ck_tenant_data_keys_kind", "kind IN ('data', 'lookup')");
                    table.CheckConstraint("ck_tenant_data_keys_number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_tenant_data_keys_tenants_tenant_id",
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
                name: "tenant_data_keys",
                schema: "platform");
        }
    }
}
