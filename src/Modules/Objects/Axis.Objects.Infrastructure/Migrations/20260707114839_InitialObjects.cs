using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Objects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "object_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_version = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "object_definition_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    object_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definition_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_definition_fields_object_definitions_object_definiti~",
                        column: x => x.object_definition_id,
                        principalTable: "object_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "object_definition_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definition_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_definition_versions_object_definitions_object_defini~",
                        column: x => x.object_definition_id,
                        principalTable: "object_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "object_definition_version_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    object_definition_version_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definition_version_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_definition_version_fields_object_definition_versions~",
                        column: x => x.object_definition_version_id,
                        principalTable: "object_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_fields_object_definition_id_field_key",
                table: "object_definition_fields",
                columns: new[] { "object_definition_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_fields_object_definition_id_sort_order",
                table: "object_definition_fields",
                columns: new[] { "object_definition_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_version_fields_object_definition_version_~",
                table: "object_definition_version_fields",
                columns: new[] { "object_definition_version_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_version_fields_object_definition_version~1",
                table: "object_definition_version_fields",
                columns: new[] { "object_definition_version_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_versions_object_definition_id_version_num~",
                table: "object_definition_versions",
                columns: new[] { "object_definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_versions_workspace_id_object_key_version_~",
                table: "object_definition_versions",
                columns: new[] { "workspace_id", "object_key", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definitions_workspace_id_object_key",
                table: "object_definitions",
                columns: new[] { "workspace_id", "object_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "object_definition_fields");

            migrationBuilder.DropTable(
                name: "object_definition_version_fields");

            migrationBuilder.DropTable(
                name: "object_definition_versions");

            migrationBuilder.DropTable(
                name: "object_definitions");
        }
    }
}
