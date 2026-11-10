using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpDocumentVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dpa_version",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "privacy_version",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "terms_version",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dpa_version",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropColumn(
                name: "privacy_version",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropColumn(
                name: "terms_version",
                schema: "control_plane",
                table: "sign_ups");
        }
    }
}
