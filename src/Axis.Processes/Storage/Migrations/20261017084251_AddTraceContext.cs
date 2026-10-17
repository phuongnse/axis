using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "span_id",
                schema: "axis",
                table: "process_work_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                schema: "axis",
                table: "process_work_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                schema: "axis",
                table: "process_step_history",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                schema: "axis",
                table: "process_call_attempts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "span_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "trace_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "trace_id",
                schema: "axis",
                table: "process_step_history");

            migrationBuilder.DropColumn(
                name: "trace_id",
                schema: "axis",
                table: "process_call_attempts");
        }
    }
}
