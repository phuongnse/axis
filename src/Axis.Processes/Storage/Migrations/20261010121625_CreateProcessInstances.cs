using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateProcessInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "process_instance_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "process_instances",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "process_start_receipts",
                schema: "axis",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_process_start_receipts", x => new { x.application_id, x.process_id, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_process_work_items_process_instance_id",
                schema: "axis",
                table: "process_work_items",
                column: "process_instance_id");

            migrationBuilder.CreateIndex(
                name: "ux_process_instances_active_subject",
                schema: "axis",
                table: "process_instances",
                columns: new[] { "application_id", "process_id", "subject_id" },
                unique: true,
                filter: "state IN ('running', 'waiting')");

            migrationBuilder.AddForeignKey(
                name: "fk_process_work_items_process_instances_process_instance_id",
                schema: "axis",
                table: "process_work_items",
                column: "process_instance_id",
                principalSchema: "axis",
                principalTable: "process_instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_process_work_items_process_instances_process_instance_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropTable(
                name: "process_instances",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "process_start_receipts",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ix_process_work_items_process_instance_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "process_instance_id",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
