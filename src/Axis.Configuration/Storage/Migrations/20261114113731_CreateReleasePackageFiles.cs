using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateReleasePackageFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_package_files",
                schema: "axis",
                columns: table => new
                {
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_package_files", x => new { x.release_id, x.path });
                    table.ForeignKey(
                        name: "fk_release_package_files_releases_release_id",
                        column: x => x.release_id,
                        principalSchema: "axis",
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The built files never change with their release: no statement may change or remove a row, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER release_package_files_no_change
                    BEFORE UPDATE OR DELETE ON axis.release_package_files
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_release_change();
                CREATE TRIGGER release_package_files_no_truncate
                    BEFORE TRUNCATE ON axis.release_package_files
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_release_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER release_package_files_no_truncate ON axis.release_package_files;
                DROP TRIGGER release_package_files_no_change ON axis.release_package_files;
                """);

            migrationBuilder.DropTable(
                name: "release_package_files",
                schema: "axis");
        }
    }
}
