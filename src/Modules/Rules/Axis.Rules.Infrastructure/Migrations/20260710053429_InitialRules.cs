using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rule_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    context_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    context_schema_version = table.Column<int>(type: "integer", nullable: false),
                    outcome_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version = table.Column<int>(type: "integer", nullable: true),
                    condition = table.Column<string>(type: "jsonb", nullable: true),
                    outcome = table.Column<string>(type: "jsonb", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rule_definition_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    context_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    context_schema_version = table.Column<int>(type: "integer", nullable: false),
                    outcome_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    condition = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<string>(type: "jsonb", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_definition_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_rule_definition_versions_rule_definitions_rule_definition_id",
                        column: x => x.rule_definition_id,
                        principalTable: "rule_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rule_definition_versions_rule_definition_id_version_number",
                table: "rule_definition_versions",
                columns: new[] { "rule_definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_definitions_workspace_id_definition_key",
                table: "rule_definitions",
                columns: new[] { "workspace_id", "definition_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_definitions_workspace_id_status_name",
                table: "rule_definitions",
                columns: new[] { "workspace_id", "status", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rule_definition_versions");

            migrationBuilder.DropTable(
                name: "rule_definitions");
        }
    }
}
