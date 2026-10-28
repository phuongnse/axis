using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRealmClientSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "realm_activation_client_secret",
                schema: "platform",
                table: "tenants",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "realm_users_client_secret",
                schema: "platform",
                table: "tenants",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "realm_activation_client_secret",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "realm_users_client_secret",
                schema: "platform",
                table: "tenants");
        }
    }
}
