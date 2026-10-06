using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateActiveSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_sites",
                schema: "axis",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_sites", x => new { x.application_id, x.path });
                    table.ForeignKey(
                        name: "fk_active_sites_active_releases_application_id",
                        column: x => x.application_id,
                        principalSchema: "axis",
                        principalTable: "active_releases",
                        principalColumn: "application_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_sites_path",
                schema: "axis",
                table: "active_sites",
                column: "path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_sites",
                schema: "axis");
        }
    }
}
