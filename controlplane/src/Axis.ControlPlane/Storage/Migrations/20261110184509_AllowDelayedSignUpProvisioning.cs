using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AllowDelayedSignUpProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups",
                sql: "provisioning IS NULL OR provisioning IN ('queued', 'creating', 'ready', 'delayed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sign_ups_provisioning",
                schema: "control_plane",
                table: "sign_ups",
                sql: "provisioning IS NULL OR provisioning IN ('queued', 'creating', 'ready')");
        }
    }
}
