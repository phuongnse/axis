using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddKeptCallValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "response",
                schema: "axis",
                table: "process_call_attempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "returned",
                schema: "axis",
                table: "package_calls",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request",
                schema: "axis",
                table: "connector_calls",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "response",
                schema: "axis",
                table: "process_call_attempts");

            migrationBuilder.DropColumn(
                name: "returned",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "request",
                schema: "axis",
                table: "connector_calls");
        }
    }
}
