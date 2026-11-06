using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateAuthoringRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authoring_requests",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    binding = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan = table.Column<string>(type: "text", nullable: true),
                    round = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    auto_accept = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_by = table.Column<string>(type: "text", nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authoring_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authoring_request_steps",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    input_summary = table.Column<string>(type: "text", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    messages = table.Column<string>(type: "jsonb", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authoring_request_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_authoring_request_steps_authoring_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "axis",
                        principalTable: "authoring_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_authoring_request_steps_request_id_ordinal",
                schema: "axis",
                table: "authoring_request_steps",
                columns: new[] { "request_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authoring_requests_application_id_sent_at_id",
                schema: "axis",
                table: "authoring_requests",
                columns: new[] { "application_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_authoring_requests_sent_at",
                schema: "axis",
                table: "authoring_requests",
                column: "sent_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authoring_request_steps",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "authoring_requests",
                schema: "axis");
        }
    }
}
