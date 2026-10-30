using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateGivenRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "given_roles",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_given_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_given_roles_user_id_application_id_role",
                schema: "axis",
                table: "given_roles",
                columns: new[] { "user_id", "application_id", "role" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "given_roles",
                schema: "axis");
        }
    }
}
