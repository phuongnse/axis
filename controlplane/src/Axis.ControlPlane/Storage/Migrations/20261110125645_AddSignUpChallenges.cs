using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sign_up_challenges",
                schema: "control_plane",
                columns: table => new
                {
                    challenge = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_up_challenges", x => x.challenge);
                    table.CheckConstraint("ck_sign_up_challenges_challenge", "octet_length(challenge) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_up_challenges_expires_at",
                schema: "control_plane",
                table: "sign_up_challenges",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sign_up_challenges",
                schema: "control_plane");
        }
    }
}
