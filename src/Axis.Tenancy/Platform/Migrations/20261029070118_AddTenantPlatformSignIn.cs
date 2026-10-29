using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPlatformSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "platform_accounts",
                schema: "platform",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tenant_platform_providers",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    issuer_binding = table.Column<string>(type: "text", nullable: true),
                    client_id_binding = table.Column<string>(type: "text", nullable: true),
                    client_secret_binding = table.Column<string>(type: "text", nullable: true),
                    client_key_binding = table.Column<string>(type: "text", nullable: true),
                    metadata_binding = table.Column<string>(type: "text", nullable: true),
                    claim_display_name = table.Column<string>(type: "text", nullable: true),
                    claim_email = table.Column<string>(type: "text", nullable: true),
                    claim_locale = table.Column<string>(type: "text", nullable: true),
                    roles_claim = table.Column<string>(type: "text", nullable: true),
                    role_values = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_platform_providers", x => new { x.tenant_id, x.name });
                    table.CheckConstraint("ck_tenant_platform_providers_type", "type IN ('oidc', 'saml')");
                    table.ForeignKey(
                        name: "fk_tenant_platform_providers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // A tenant that exists keeps the sign-in it has, which is its realm accounts.
            migrationBuilder.Sql("UPDATE platform.tenants SET platform_accounts = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_platform_providers",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "platform_accounts",
                schema: "platform",
                table: "tenants");
        }
    }
}
