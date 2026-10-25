using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letters",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurrence_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    settled_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_failures",
                schema: "axis",
                columns: table => new
                {
                    kind = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_failures", x => new { x.kind, x.target });
                });

            migrationBuilder.CreateIndex(
                name: "ix_dead_letters_application_id_process_id_state",
                schema: "axis",
                table: "dead_letters",
                columns: new[] { "application_id", "process_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letters",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "delivery_failures",
                schema: "axis");
        }
    }
}
