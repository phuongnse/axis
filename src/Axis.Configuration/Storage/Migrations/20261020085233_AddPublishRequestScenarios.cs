using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRequestScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scenario_names",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scenario_results",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scenario_names",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "scenario_results",
                schema: "axis",
                table: "publish_requests");
        }
    }
}
