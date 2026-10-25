using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                schema: "axis",
                table: "process_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_at",
                schema: "axis",
                table: "process_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_process_work_items_task_id",
                schema: "axis",
                table: "process_work_items",
                column: "task_id");

            migrationBuilder.AddForeignKey(
                name: "fk_process_work_items_process_tasks_task_id",
                schema: "axis",
                table: "process_work_items",
                column: "task_id",
                principalSchema: "axis",
                principalTable: "process_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_process_work_items_process_tasks_task_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropIndex(
                name: "ix_process_work_items_task_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "task_id",
                schema: "axis",
                table: "process_work_items");

            migrationBuilder.DropColumn(
                name: "expired_at",
                schema: "axis",
                table: "process_tasks");
        }
    }
}
