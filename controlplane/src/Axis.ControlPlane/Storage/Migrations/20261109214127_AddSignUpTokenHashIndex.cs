using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpTokenHashIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_sign_ups_token_hash",
                schema: "control_plane",
                table: "sign_ups",
                column: "token_hash",
                unique: true,
                filter: "token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sign_ups_token_hash",
                schema: "control_plane",
                table: "sign_ups");
        }
    }
}
