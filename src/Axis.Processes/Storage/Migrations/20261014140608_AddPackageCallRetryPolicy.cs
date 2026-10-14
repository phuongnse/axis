using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageCallRetryPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "idempotent",
                schema: "axis",
                table: "package_calls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "retry_attempts",
                schema: "axis",
                table: "package_calls",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "retry_backoff",
                schema: "axis",
                table: "package_calls",
                type: "interval",
                nullable: false,
                defaultValueSql: "'30 seconds'::interval");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "idempotent",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "retry_attempts",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "retry_backoff",
                schema: "axis",
                table: "package_calls");
        }
    }
}
