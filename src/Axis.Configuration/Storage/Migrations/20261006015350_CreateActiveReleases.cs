using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateActiveReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_releases",
                schema: "axis",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_releases", x => x.application_id);
                    table.ForeignKey(
                        name: "fk_active_releases_releases_release_id",
                        column: x => x.release_id,
                        principalSchema: "axis",
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_releases_release_id",
                schema: "axis",
                table: "active_releases",
                column: "release_id");

            // The model cannot express an index on an expression, so the name index is raw SQL.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_active_releases_lower_name ON axis.active_releases (lower(name))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX axis.ix_active_releases_lower_name");

            migrationBuilder.DropTable(
                name: "active_releases",
                schema: "axis");
        }
    }
}
