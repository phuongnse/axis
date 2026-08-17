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
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    installed_solution_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_component_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    installed_component_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    installed_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_lease_epoch = table.Column<long>(type: "bigint", nullable: true),
                    input_mappings = table.Column<string>(type: "jsonb", nullable: false),
                    revision_history = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_bindings", x => x.id);
                    table.CheckConstraint("CK_rule_bindings_installed_provenance", "(installed_solution_version_id IS NULL AND installed_component_key IS NULL AND\n installed_component_hash IS NULL AND installed_operation_id IS NULL AND\n installed_step_id IS NULL AND installed_lease_epoch IS NULL)\nOR\n(installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND\n installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND\n installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND\n installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND\n installed_component_hash ~ '^[0-9a-f]{64}$' AND\n installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
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
                    revision = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version = table.Column<int>(type: "integer", nullable: true),
                    active_version = table.Column<int>(type: "integer", nullable: true),
                    condition = table.Column<string>(type: "jsonb", nullable: true),
                    output = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_by_actor_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    archived_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    archived_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    search_text = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, '')))", stored: true),
                    search_title = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '')))", stored: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, ''))))", stored: true),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
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
                    published_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    published_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rule_bindings_workspace_id_definition_key_definition_version",
                table: "rule_bindings",
                columns: new[] { "workspace_id", "definition_key", "definition_version" });

            migrationBuilder.CreateIndex(
                name: "IX_rule_bindings_workspace_id_installed_component_key",
                table: "rule_bindings",
                columns: new[] { "workspace_id", "installed_component_key" },
                unique: true,
                filter: "installed_component_key IS NOT NULL");

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
                name: "IX_rule_definitions_workspace_id_archived_at_active_version_la~",
                table: "rule_definitions",
                columns: new[] { "workspace_id", "archived_at", "active_version", "latest_published_version", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_rule_definitions_workspace_id_definition_key",
                table: "rule_definitions",
                columns: new[] { "workspace_id", "definition_key" },
                unique: true);
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

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS axis_unaccent(text);");
        }
    }
}
