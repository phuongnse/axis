using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provisioning",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "secret_hash",
                schema: "control_plane",
                table: "sign_ups",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups",
                sql: "provisioning IS NULL OR provisioning IN ('queued', 'creating', 'ready')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sign_ups_secret_hash",
                schema: "control_plane",
                table: "sign_ups",
                sql: "secret_hash IS NULL OR octet_length(secret_hash) = 32");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sign_ups_secret_hash",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropColumn(
                name: "provisioning",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropColumn(
                name: "secret_hash",
                schema: "control_plane",
                table: "sign_ups");
        }
    }
}
