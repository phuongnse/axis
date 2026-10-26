using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSubProcessParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_instance_id",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_step",
                schema: "axis",
                table: "process_instances",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_process_instances_parent_instance_id",
                schema: "axis",
                table: "process_instances",
                column: "parent_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_process_instances_parent_instance_id",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropColumn(
                name: "parent_instance_id",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropColumn(
                name: "parent_step",
                schema: "axis",
                table: "process_instances");
        }
    }
}
