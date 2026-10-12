using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateConnectorCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "connector_call_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "connector_calls",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector = table.Column<string>(type: "text", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "jsonb", nullable: true),
                    last_status = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_connector_calls_process_instances_process_instance_id",
                        column: x => x.process_instance_id,
                        principalSchema: "axis",
                        principalTable: "process_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_process_work_items_connector_call_id",
                schema: "axis",
                table: "process_work_items",
                column: "connector_call_id");

            migrationBuilder.CreateIndex(
                name: "ux_connector_calls_process_instance_id_step",
                schema: "axis",
                table: "connector_calls",
                columns: new[] { "process_instance_id", "step" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_process_work_items_connector_calls_connector_call_id",
                schema: "axis",
                table: "process_work_items",
                column: "connector_call_id",
                principalSchema: "axis",
                principalTable: "connector_calls",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_process_work_items_connector_calls_connector_call_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropTable(
                name: "connector_calls",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ix_process_work_items_connector_call_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "connector_call_id",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
