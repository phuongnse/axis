using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddStepHistoryRunValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "changes",
                schema: "axis",
                table: "process_step_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "read",
                schema: "axis",
                table: "process_step_history",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "changes",
                schema: "axis",
                table: "process_step_history");

            migrationBuilder.DropColumn(
                name: "read",
                schema: "axis",
                table: "process_step_history");
        }
    }
}
