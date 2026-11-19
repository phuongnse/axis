using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddStepProblems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "problem",
                schema: "axis",
                table: "process_step_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "problem",
                schema: "axis",
                table: "process_call_attempts",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "problem",
                schema: "axis",
                table: "process_step_history");

            migrationBuilder.DropColumn(
                name: "problem",
                schema: "axis",
                table: "process_call_attempts");
        }
    }
}
