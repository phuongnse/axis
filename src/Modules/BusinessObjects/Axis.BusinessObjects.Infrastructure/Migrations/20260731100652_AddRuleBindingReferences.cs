using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleBindingReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_object_definition_version_field_rules_business_obj~",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropIndex(
                name: "IX_business_object_definition_field_rules_business_object_fiel~",
                table: "business_object_definition_field_rules");

            migrationBuilder.DropColumn(
                name: "definition_key",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "definition_version",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "inputs",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "definition_key",
                table: "business_object_definition_field_rules");

            migrationBuilder.DropColumn(
                name: "definition_version",
                table: "business_object_definition_field_rules");

            migrationBuilder.DropColumn(
                name: "inputs",
                table: "business_object_definition_field_rules");

            migrationBuilder.AddColumn<Guid>(
                name: "binding_id",
                table: "business_object_definition_version_field_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "binding_id",
                table: "business_object_definition_field_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_rules_business_obj~",
                table: "business_object_definition_version_field_rules",
                columns: new[] { "business_object_definition_version_field_id", "binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_rules_business_object_fiel~",
                table: "business_object_definition_field_rules",
                columns: new[] { "business_object_field_definition_id", "binding_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_object_definition_version_field_rules_business_obj~",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropIndex(
                name: "IX_business_object_definition_field_rules_business_object_fiel~",
                table: "business_object_definition_field_rules");

            migrationBuilder.DropColumn(
                name: "binding_id",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "binding_id",
                table: "business_object_definition_field_rules");

            migrationBuilder.AddColumn<string>(
                name: "definition_key",
                table: "business_object_definition_version_field_rules",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "definition_version",
                table: "business_object_definition_version_field_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "inputs",
                table: "business_object_definition_version_field_rules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "definition_key",
                table: "business_object_definition_field_rules",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "definition_version",
                table: "business_object_definition_field_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "inputs",
                table: "business_object_definition_field_rules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_version_field_rules_business_obj~",
                table: "business_object_definition_version_field_rules",
                columns: new[] { "business_object_definition_version_field_id", "definition_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_definition_field_rules_business_object_fiel~",
                table: "business_object_definition_field_rules",
                columns: new[] { "business_object_field_definition_id", "definition_key" },
                unique: true);
        }
    }
}
