using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_process_instances_application_id_process_id_created_at_id",
                schema: "axis",
                table: "process_instances",
                columns: new[] { "application_id", "process_id", "created_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_process_instances_application_id_process_id_state_created_at",
                schema: "axis",
                table: "process_instances",
                columns: new[] { "application_id", "process_id", "state", "created_at", "id" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_process_instances_subject_id",
                schema: "axis",
                table: "process_instances",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_process_instances_application_id_process_id_created_at_id",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropIndex(
                name: "ix_process_instances_application_id_process_id_state_created_at",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropIndex(
                name: "ix_process_instances_subject_id",
                schema: "axis",
                table: "process_instances");
        }
    }
}
