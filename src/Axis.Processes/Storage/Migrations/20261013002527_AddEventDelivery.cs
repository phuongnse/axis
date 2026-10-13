using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trigger_starts",
                schema: "axis",
                columns: table => new
                {
                    trigger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trigger_starts", x => new { x.trigger_id, x.source, x.subject_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_process_work_items_event_id",
                schema: "axis",
                table: "process_work_items",
                column: "event_id");

            migrationBuilder.AddForeignKey(
                name: "fk_process_work_items_events_event_id",
                schema: "axis",
                table: "process_work_items",
                column: "event_id",
                principalSchema: "axis",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_process_work_items_events_event_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropTable(
                name: "trigger_starts",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ix_process_work_items_event_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "event_id",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
