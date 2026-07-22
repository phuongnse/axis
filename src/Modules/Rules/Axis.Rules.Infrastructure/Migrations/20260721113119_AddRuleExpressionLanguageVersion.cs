using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleExpressionLanguageVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "expression_language_version",
                table: "rule_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "expression_language_version",
                table: "rule_definition_versions",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expression_language_version",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "expression_language_version",
                table: "rule_definition_versions");
        }
    }
}
