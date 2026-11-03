using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLegalLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "privacy_url",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "terms_url",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "terms_version",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_site_legal_links",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    site_path = table.Column<string>(type: "text", nullable: false),
                    privacy_url = table.Column<string>(type: "text", nullable: true),
                    terms_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_site_legal_links", x => new { x.tenant_id, x.site_path });
                    table.CheckConstraint("ck_tenant_site_legal_links_url", "privacy_url IS NOT NULL OR terms_url IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_tenant_site_legal_links_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_terms_version",
                schema: "platform",
                table: "tenants",
                sql: "(terms_url IS NULL) = (terms_version IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_site_legal_links",
                schema: "platform");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_terms_version",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "privacy_url",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "terms_url",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "terms_version",
                schema: "platform",
                table: "tenants");
        }
    }
}
