using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRequestPackageTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "package",
                schema: "axis",
                table: "publish_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_tests",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "package",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "package_tests",
                schema: "axis",
                table: "publish_requests");
        }
    }
}
