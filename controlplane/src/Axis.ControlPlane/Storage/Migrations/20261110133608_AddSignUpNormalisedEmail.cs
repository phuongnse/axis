using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpNormalisedEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalised_email",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: true);

            // The rule of SignUpEmail.Normalise in SQL: the +tag of the part before the last @ is cut and the whole address is lower case.
            migrationBuilder.Sql(
                "UPDATE control_plane.sign_ups SET normalised_email = lower(regexp_replace(email, '\\+[^@]*(@[^@]*)$', '\\1'))");

            migrationBuilder.AlterColumn<string>(
                name: "normalised_email",
                schema: "control_plane",
                table: "sign_ups",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_sign_ups_verified_email",
                schema: "control_plane",
                table: "sign_ups",
                column: "normalised_email",
                unique: true,
                filter: "state = 'verified'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sign_ups_verified_email",
                schema: "control_plane",
                table: "sign_ups");

            migrationBuilder.DropColumn(
                name: "normalised_email",
                schema: "control_plane",
                table: "sign_ups");
        }
    }
}
