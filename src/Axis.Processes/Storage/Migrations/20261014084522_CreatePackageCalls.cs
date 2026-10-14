using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreatePackageCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "package_call_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "package_calls",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "jsonb", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_package_calls_process_instances_process_instance_id",
                        column: x => x.process_instance_id,
                        principalSchema: "axis",
                        principalTable: "process_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_process_work_items_package_call_id",
                schema: "axis",
                table: "process_work_items",
                column: "package_call_id");

            migrationBuilder.CreateIndex(
                name: "ux_package_calls_process_instance_id_step",
                schema: "axis",
                table: "package_calls",
                columns: new[] { "process_instance_id", "step" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_process_work_items_package_calls_package_call_id",
                schema: "axis",
                table: "process_work_items",
                column: "package_call_id",
                principalSchema: "axis",
                principalTable: "package_calls",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_process_work_items_package_calls_package_call_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropTable(
                name: "package_calls",
                schema: "axis");

            migrationBuilder.DropIndex(
                name: "ix_process_work_items_package_call_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "package_call_id",
                schema: "axis",
                table: "process_work_items");
        }
    }
}
