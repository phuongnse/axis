using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSmtpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_from",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_host",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "smtp_password",
                schema: "platform",
                table: "tenants",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "smtp_port",
                schema: "platform",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "smtp_use_tls",
                schema: "platform",
                table: "tenants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_user_name",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_smtp_from",
                schema: "platform",
                table: "tenants",
                sql: "(smtp_host IS NULL) = (email_from IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_smtp_host",
                schema: "platform",
                table: "tenants",
                sql: "smtp_host IS NOT NULL OR (smtp_port IS NULL AND smtp_use_tls IS NULL AND smtp_user_name IS NULL AND smtp_password IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_smtp_from",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_smtp_host",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "email_from",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "smtp_host",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "smtp_password",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "smtp_port",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "smtp_use_tls",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "smtp_user_name",
                schema: "platform",
                table: "tenants");
        }
    }
}
