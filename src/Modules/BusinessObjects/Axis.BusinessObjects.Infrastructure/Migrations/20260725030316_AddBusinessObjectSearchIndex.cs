using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessObjectSearchIndex : Migration
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS axis_unaccent(text);
                DROP EXTENSION IF EXISTS pg_trgm;
                DROP EXTENSION IF EXISTS unaccent;
                """);
        }
    }
}
