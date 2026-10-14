using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggerSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurrence_at",
                schema: "axis",
                table: "process_work_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "release_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trigger_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trigger_schedules",
                schema: "axis",
                columns: table => new
                {
                    trigger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_occurrence_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_occurrence_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trigger_schedules", x => x.trigger_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_process_work_items_schedule_trigger_id",
                schema: "axis",
                table: "process_work_items",
                column: "trigger_id",
                unique: true,
                filter: "kind = 'trigger.schedule'");

            migrationBuilder.CreateIndex(
                name: "ix_trigger_schedules_application_id",
                schema: "axis",
                table: "trigger_schedules",
                column: "application_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trigger_schedules",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ux_process_work_items_schedule_trigger_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "occurrence_at",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "release_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "trigger_id",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
