using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "schedule",
                schema: "axis",
                table: "trigger_schedules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "axis",
                table: "trigger_schedules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cursor",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "schedule",
                schema: "axis",
                table: "trigger_schedules");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "axis",
                table: "trigger_schedules");

            migrationBuilder.DropColumn(
                name: "cursor",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
