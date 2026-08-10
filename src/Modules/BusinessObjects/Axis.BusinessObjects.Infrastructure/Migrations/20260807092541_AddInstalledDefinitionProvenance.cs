using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalledDefinitionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropDefinitionSearchProjection(migrationBuilder);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "business_object_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            AddDefinitionSearchProjection(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "installed_component_hash",
                table: "business_object_definitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "installed_component_key",
                table: "business_object_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "installed_lease_epoch",
                table: "business_object_definitions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_operation_id",
                table: "business_object_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_solution_version_id",
                table: "business_object_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_step_id",
                table: "business_object_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "business_object_definition_versions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_version_fields",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "installed_binding_key",
                table: "business_object_definition_version_field_rules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_version_field_choice_options",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_fields",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "installed_binding_key",
                table: "business_object_definition_field_rules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_field_choice_options",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definitions_workspace_id_installed_componen~",
                table: "business_object_definitions",
                columns: new[] { "workspace_id", "installed_component_key" },
                unique: true,
                filter: "installed_component_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_business_object_definitions_installed_provenance",
                table: "business_object_definitions",
                sql: "(installed_solution_version_id IS NULL AND installed_component_key IS NULL AND\n installed_component_hash IS NULL AND installed_operation_id IS NULL AND\n installed_step_id IS NULL AND installed_lease_epoch IS NULL)\nOR\n(installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND\n installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND\n installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND\n installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND\n installed_component_hash ~ '^[0-9a-f]{64}$' AND\n installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_object_definitions_workspace_id_installed_componen~",
                table: "business_object_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_business_object_definitions_installed_provenance",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_component_hash",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_component_key",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_lease_epoch",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_operation_id",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_solution_version_id",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_step_id",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "installed_binding_key",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "installed_binding_key",
                table: "business_object_definition_field_rules");

            DropDefinitionSearchProjection(migrationBuilder);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "business_object_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            AddDefinitionSearchProjection(migrationBuilder);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "business_object_definition_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_version_fields",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_version_field_choice_options",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_fields",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                table: "business_object_definition_field_choice_options",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }

        private static void DropDefinitionSearchProjection(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_business_object_definitions_search_text",
                table: "business_object_definitions");
            migrationBuilder.DropIndex(
                name: "ix_business_object_definitions_search_title",
                table: "business_object_definitions");
            migrationBuilder.DropIndex(
                name: "ix_business_object_definitions_search_vector",
                table: "business_object_definitions");
            migrationBuilder.DropColumn(
                name: "search_text",
                table: "business_object_definitions");
            migrationBuilder.DropColumn(
                name: "search_title",
                table: "business_object_definitions");
            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "business_object_definitions");
        }

        private static void AddDefinitionSearchProjection(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "business_object_definitions",
                type: "text",
                nullable: true,
                computedColumnSql: "axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, '')))",
                stored: true);
            migrationBuilder.AddColumn<string>(
                name: "search_title",
                table: "business_object_definitions",
                type: "text",
                nullable: true,
                computedColumnSql: "axis_unaccent(lower(coalesce(name, '')))",
                stored: true);
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "business_object_definitions",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(object_key, ''))))",
                stored: true);
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
        }
    }
}
