using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;
                CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
                CREATE OR REPLACE FUNCTION axis_unaccent(input text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $function$
                    SELECT public.unaccent('public.unaccent'::regdictionary, input)
                $function$;
                """);

            migrationBuilder.CreateTable(
                name: "rule_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    target_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    use_case_or_trigger = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    failure_behavior = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    input_mappings = table.Column<string>(type: "jsonb", nullable: false),
                    revision_history = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rule_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expression_language_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version = table.Column<int>(type: "integer", nullable: true),
                    condition = table.Column<string>(type: "jsonb", nullable: true),
                    output = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    search_text = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, '')))", stored: true),
                    search_title = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '')))", stored: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, ''))))", stored: true),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
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
                    expression_language_version = table.Column<int>(type: "integer", nullable: false),
                    condition = table.Column<string>(type: "jsonb", nullable: false),
                    output = table.Column<string>(type: "jsonb", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inputs = table.Column<string>(type: "jsonb", nullable: false)
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
                name: "IX_rule_bindings_workspace_id_definition_key_definition_version",
                table: "rule_bindings",
                columns: new[] { "workspace_id", "definition_key", "definition_version" });

            migrationBuilder.CreateIndex(
                name: "IX_rule_bindings_workspace_id_target_type_target_id_use_case_o~",
                table: "rule_bindings",
                columns: new[] { "workspace_id", "target_type", "target_id", "use_case_or_trigger", "definition_key", "definition_version" });

            migrationBuilder.CreateIndex(
                name: "IX_rule_definition_versions_rule_definition_id_version_number",
                table: "rule_definition_versions",
                columns: new[] { "rule_definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rule_definitions_search_text",
                table: "rule_definitions",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_definitions_search_title",
                table: "rule_definitions",
                column: "search_title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_definitions_search_vector",
                table: "rule_definitions",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

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
                name: "rule_bindings");

            migrationBuilder.DropTable(
                name: "rule_definition_versions");

            migrationBuilder.DropTable(
                name: "rule_definitions");
        }
    }
}
