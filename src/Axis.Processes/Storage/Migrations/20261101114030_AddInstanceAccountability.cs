using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceAccountability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accountable_user",
                schema: "axis",
                table: "process_instances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decision_task",
                schema: "axis",
                table: "process_instances",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accountable_user",
                schema: "axis",
                table: "process_instances");

            migrationBuilder.DropColumn(
                name: "decision_task",
                schema: "axis",
                table: "process_instances");
        }
    }
}
