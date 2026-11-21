using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRequestRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "input",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation",
                schema: "axis",
                table: "publish_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "input",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "operation",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "run",
                schema: "axis",
                table: "publish_requests");
        }
    }
}
