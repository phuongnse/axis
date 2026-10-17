using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateComputedFieldErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "computed_field_errors",
                schema: "axis",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "text", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    error = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_computed_field_errors", x => new { x.entity_id, x.record_id, x.field });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "computed_field_errors",
                schema: "axis");
        }
    }
}
