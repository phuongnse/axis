using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_object_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    latest_published_version_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_object_definition_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    definition_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    business_object_field_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
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
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    definition_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    business_object_definition_version_field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
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
                columns: new[] { "business_object_field_definition_id", "definition_key" },
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
                columns: new[] { "business_object_definition_version_field_id", "definition_key" },
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
                name: "IX_business_object_definitions_workspace_id_object_key",
                table: "business_object_definitions",
                columns: new[] { "workspace_id", "object_key" },
                unique: true);
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
                name: "business_object_definition_fields");

            migrationBuilder.DropTable(
                name: "business_object_definition_version_fields");

            migrationBuilder.DropTable(
                name: "business_object_definition_versions");

            migrationBuilder.DropTable(
                name: "business_object_definitions");
        }
    }
}
