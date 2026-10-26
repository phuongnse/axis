using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AllowInstancesWithoutSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_process_instances_active_subject",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_entity_id",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "parameters",
                schema: "axis",
                table: "process_instances",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_process_instances_active_subject",
                schema: "axis",
                table: "process_instances",
                columns: new[] { "application_id", "process_id", "subject_id" },
                unique: true,
                filter: "state IN ('running', 'waiting')")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_process_instances_active_subject",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropColumn(
                name: "parameters",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_entity_id",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_process_instances_active_subject",
                schema: "axis",
                table: "process_instances",
                columns: new[] { "application_id", "process_id", "subject_id" },
                unique: true,
                filter: "state IN ('running', 'waiting')");
        }
    }
}
