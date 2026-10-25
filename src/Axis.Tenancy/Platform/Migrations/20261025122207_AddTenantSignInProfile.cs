using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSignInProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "oidc_client_key",
                schema: "platform",
                table: "tenants",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_client_key_id",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "oidc_previous_client_key",
                schema: "platform",
                table: "tenants",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_previous_client_key_id",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_profile",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "standard");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_oidc_client_key",
                schema: "platform",
                table: "tenants",
                sql: "(oidc_client_key IS NULL) = (oidc_client_key_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_oidc_previous_client_key",
                schema: "platform",
                table: "tenants",
                sql: "(oidc_previous_client_key IS NULL) = (oidc_previous_client_key_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_oidc_previous_needs_current",
                schema: "platform",
                table: "tenants",
                sql: "oidc_previous_client_key IS NULL OR oidc_client_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_oidc_profile",
                schema: "platform",
                table: "tenants",
                sql: "oidc_profile IN ('standard', 'fapi2')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_oidc_client_key",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_oidc_previous_client_key",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_oidc_previous_needs_current",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_oidc_profile",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "oidc_client_key",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "oidc_client_key_id",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "oidc_previous_client_key",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "oidc_previous_client_key_id",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "oidc_profile",
                schema: "platform",
                table: "tenants");
        }
    }
}
