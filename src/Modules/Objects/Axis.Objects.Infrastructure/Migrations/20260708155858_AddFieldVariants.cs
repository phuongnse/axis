using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Objects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "field_type",
                table: "object_definition_version_fields",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Text");

            migrationBuilder.AddColumn<string>(
                name: "field_type",
                table: "object_definition_fields",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Text");

            migrationBuilder.CreateTable(
                name: "object_definition_field_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    min_number = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_number = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    min_date = table.Column<DateOnly>(type: "date", nullable: true),
                    max_date = table.Column<DateOnly>(type: "date", nullable: true),
                    min_length = table.Column<int>(type: "integer", nullable: true),
                    max_length = table.Column<int>(type: "integer", nullable: true),
                    pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    object_field_definition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definition_field_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_definition_field_variants_object_definition_fields_o~",
                        column: x => x.object_field_definition_id,
                        principalTable: "object_definition_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "object_definition_version_field_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    min_number = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_number = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    min_date = table.Column<DateOnly>(type: "date", nullable: true),
                    max_date = table.Column<DateOnly>(type: "date", nullable: true),
                    min_length = table.Column<int>(type: "integer", nullable: true),
                    max_length = table.Column<int>(type: "integer", nullable: true),
                    pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    object_definition_version_field_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_definition_version_field_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_definition_version_field_variants_object_definition_~",
                        column: x => x.object_definition_version_field_id,
                        principalTable: "object_definition_version_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_field_variants_object_field_definition_i~1",
                table: "object_definition_field_variants",
                columns: new[] { "object_field_definition_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_field_variants_object_field_definition_id~",
                table: "object_definition_field_variants",
                columns: new[] { "object_field_definition_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_version_field_variants_object_definition_~",
                table: "object_definition_version_field_variants",
                columns: new[] { "object_definition_version_field_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_definition_version_field_variants_object_definition~1",
                table: "object_definition_version_field_variants",
                columns: new[] { "object_definition_version_field_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "object_definition_field_variants");

            migrationBuilder.DropTable(
                name: "object_definition_version_field_variants");

            migrationBuilder.DropColumn(
                name: "field_type",
                table: "object_definition_version_fields");

            migrationBuilder.DropColumn(
                name: "field_type",
                table: "object_definition_fields");
        }
    }
}
