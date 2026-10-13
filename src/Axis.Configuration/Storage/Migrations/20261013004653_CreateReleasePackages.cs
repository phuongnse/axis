using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateReleasePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_packages",
                schema: "axis",
                columns: table => new
                {
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_packages", x => new { x.release_id, x.package_id });
                    table.ForeignKey(
                        name: "fk_release_packages_releases_release_id",
                        column: x => x.release_id,
                        principalSchema: "axis",
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "release_packages",
                schema: "axis");
        }
    }
}
