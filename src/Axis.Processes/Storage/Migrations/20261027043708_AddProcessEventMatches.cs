using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessEventMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "process_event_matches",
                schema: "axis",
                columns: table => new
                {
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_event_matches", x => new { x.process_instance_id, x.event_id });
                    table.ForeignKey(
                        name: "fk_process_event_matches_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "axis",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_process_event_matches_process_instances_process_instance_id",
                        column: x => x.process_instance_id,
                        principalSchema: "axis",
                        principalTable: "process_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_subject_id_raised_at",
                schema: "axis",
                table: "events",
                columns: new[] { "subject_id", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_process_event_matches_event_id",
                schema: "axis",
                table: "process_event_matches",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "process_event_matches",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ix_events_subject_id_raised_at",
                schema: "axis",
                table: "events");
        }
    }
}
