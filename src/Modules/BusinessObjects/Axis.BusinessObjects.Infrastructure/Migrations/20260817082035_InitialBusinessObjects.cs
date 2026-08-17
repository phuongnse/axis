using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessObjects : Migration
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
                name: "business_object_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    installed_solution_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_component_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    installed_component_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    installed_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_lease_epoch = table.Column<long>(type: "bigint", nullable: true),
                    search_text = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, '')))", stored: true),
                    search_title = table.Column<string>(type: "text", nullable: true, computedColumnSql: "axis_unaccent(lower(coalesce(name, '')))", stored: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, ''))))", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definitions", x => x.id);
                    table.CheckConstraint("CK_business_object_definitions_installed_provenance", "(installed_solution_version_id IS NULL AND installed_component_key IS NULL AND\n installed_component_hash IS NULL AND installed_operation_id IS NULL AND\n installed_step_id IS NULL AND installed_lease_epoch IS NULL)\nOR\n(installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND\n installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND\n installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND\n installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND\n installed_component_hash ~ '^[0-9a-f]{64}$' AND\n installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                });

            migrationBuilder.CreateTable(
                name: "business_object_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version_number = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    submitted_by_subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rule_evaluations = table.Column<string>(type: "jsonb", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    choice_selection_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    business_object_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_fields_business_object_definitio~",
                        column: x => x.business_object_definition_id,
                        principalTable: "business_object_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_by_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_versions_business_object_definit~",
                        column: x => x.source_definition_id,
                        principalTable: "business_object_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_field_choice_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    business_object_field_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_field_choice_options", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_field_choice_options_business_ob~",
                        column: x => x.business_object_field_definition_id,
                        principalTable: "business_object_definition_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_field_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_revision = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    installed_binding_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    business_object_field_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_field_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_field_rules_business_object_defi~",
                        column: x => x.business_object_field_definition_id,
                        principalTable: "business_object_definition_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_version_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_field_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    choice_selection_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    business_object_definition_version_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_version_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_version_fields_business_object_d~",
                        column: x => x.business_object_definition_version_id,
                        principalTable: "business_object_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_version_field_choice_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_choice_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    business_object_definition_version_field_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_version_field_choice_options", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_version_field_choice_options_bus~",
                        column: x => x.business_object_definition_version_field_id,
                        principalTable: "business_object_definition_version_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_version_field_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_field_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_revision = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    installed_binding_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    business_object_definition_version_field_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definition_version_field_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_object_definition_version_field_rules_business_obj~",
                        column: x => x.business_object_definition_version_field_id,
                        principalTable: "business_object_definition_version_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_choice_options_business_o~1",
                table: "business_object_definition_field_choice_options",
                columns: new[] { "business_object_field_definition_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_choice_options_business_ob~",
                table: "business_object_definition_field_choice_options",
                columns: new[] { "business_object_field_definition_id", "option_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_rules_business_object_fie~1",
                table: "business_object_definition_field_rules",
                columns: new[] { "business_object_field_definition_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_rules_business_object_fiel~",
                table: "business_object_definition_field_rules",
                columns: new[] { "business_object_field_definition_id", "binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_fields_business_object_definiti~1",
                table: "business_object_definition_fields",
                columns: new[] { "business_object_definition_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_fields_business_object_definitio~",
                table: "business_object_definition_fields",
                columns: new[] { "business_object_definition_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_choice_options_bu~1",
                table: "business_object_definition_version_field_choice_options",
                columns: new[] { "business_object_definition_version_field_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_choice_options_bu~2",
                table: "business_object_definition_version_field_choice_options",
                columns: new[] { "business_object_definition_version_field_id", "source_choice_option_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_choice_options_bus~",
                table: "business_object_definition_version_field_choice_options",
                columns: new[] { "business_object_definition_version_field_id", "option_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_rules_business_ob~1",
                table: "business_object_definition_version_field_rules",
                columns: new[] { "business_object_definition_version_field_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_rules_business_ob~2",
                table: "business_object_definition_version_field_rules",
                columns: new[] { "business_object_definition_version_field_id", "source_field_rule_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_rules_business_obj~",
                table: "business_object_definition_version_field_rules",
                columns: new[] { "business_object_definition_version_field_id", "binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_fields_business_object_~1",
                table: "business_object_definition_version_fields",
                columns: new[] { "business_object_definition_version_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_fields_business_object_~2",
                table: "business_object_definition_version_fields",
                columns: new[] { "business_object_definition_version_id", "source_field_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_fields_business_object_d~",
                table: "business_object_definition_version_fields",
                columns: new[] { "business_object_definition_version_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_versions_source_definition_id_ve~",
                table: "business_object_definition_versions",
                columns: new[] { "source_definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_versions_workspace_id_object_key~",
                table: "business_object_definition_versions",
                columns: new[] { "workspace_id", "object_key", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_object_definitions_search_text",
                table: "business_object_definitions",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_business_object_definitions_search_title",
                table: "business_object_definitions",
                column: "search_title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_business_object_definitions_search_vector",
                table: "business_object_definitions",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definitions_workspace_id_installed_componen~",
                table: "business_object_definitions",
                columns: new[] { "workspace_id", "installed_component_key" },
                unique: true,
                filter: "installed_component_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definitions_workspace_id_object_key",
                table: "business_object_definitions",
                columns: new[] { "workspace_id", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_definition_version_id",
                table: "business_object_records",
                columns: new[] { "workspace_id", "definition_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_object_key_idempotency~",
                table: "business_object_records",
                columns: new[] { "workspace_id", "object_key", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_object_key_updated_at",
                table: "business_object_records",
                columns: new[] { "workspace_id", "object_key", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_object_definition_field_choice_options");

            migrationBuilder.DropTable(
                name: "business_object_definition_field_rules");

            migrationBuilder.DropTable(
                name: "business_object_definition_version_field_choice_options");

            migrationBuilder.DropTable(
                name: "business_object_definition_version_field_rules");

            migrationBuilder.DropTable(
                name: "business_object_records");

            migrationBuilder.DropTable(
                name: "business_object_definition_fields");

            migrationBuilder.DropTable(
                name: "business_object_definition_version_fields");

            migrationBuilder.DropTable(
                name: "business_object_definition_versions");

            migrationBuilder.DropTable(
                name: "business_object_definitions");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS axis_unaccent(text);");
        }
    }
}
