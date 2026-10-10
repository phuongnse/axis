using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateProcessTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "process_tasks",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    subject_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_kind = table.Column<string>(type: "text", nullable: false),
                    assignee = table.Column<string>(type: "text", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    completed_by = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_process_tasks_process_instances_process_instance_id",
                        column: x => x.process_instance_id,
                        principalSchema: "axis",
                        principalTable: "process_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_process_tasks_application_id_state_assignee",
                schema: "axis",
                table: "process_tasks",
                columns: new[] { "application_id", "state", "assignee_kind", "assignee" });

            migrationBuilder.CreateIndex(
                name: "ix_process_tasks_process_instance_id",
                schema: "axis",
                table: "process_tasks",
                column: "process_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "process_tasks",
                schema: "axis");
        }
    }
}
