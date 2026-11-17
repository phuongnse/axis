using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageCallLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lease_id",
                schema: "axis",
                table: "package_calls",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_until",
                schema: "axis",
                table: "package_calls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "runner_id",
                schema: "axis",
                table: "package_calls",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lease_id",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "lease_until",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "runner_id",
                schema: "axis",
                table: "package_calls");
        }
    }
}
