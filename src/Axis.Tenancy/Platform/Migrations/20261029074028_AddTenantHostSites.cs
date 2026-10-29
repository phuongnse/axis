using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantHostSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "site_path",
                schema: "platform",
                table: "tenant_hosts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_hosts_tenant_id_site_path",
                schema: "platform",
                table: "tenant_hosts",
                columns: new[] { "tenant_id", "site_path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_hosts_tenant_id_site_path",
                schema: "platform",
                table: "tenant_hosts");

            migrationBuilder.DropColumn(
                name: "site_path",
                schema: "platform",
                table: "tenant_hosts");
        }
    }
}
