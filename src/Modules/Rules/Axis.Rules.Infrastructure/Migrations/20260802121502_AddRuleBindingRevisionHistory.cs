using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleBindingRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revision_history",
                table: "rule_bindings",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.Sql(
                """
                UPDATE rule_bindings
                SET revision_history = jsonb_build_array(
                    jsonb_build_object(
                        'revision', revision,
                        'definitionKey', definition_key,
                        'definitionVersion', definition_version,
                        'targetType', target_type,
                        'targetId', target_id,
                        'useCaseOrTrigger', use_case_or_trigger,
                        'inputMappings', input_mappings,
                        'priority', priority,
                        'enabled', enabled,
                        'failureBehavior', CASE failure_behavior
                            WHEN 'FailOpen' THEN 1
                            ELSE 0
                        END,
                        'updatedByUserId', updated_by_user_id,
                        'updatedAt', updated_at))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revision_history",
                table: "rule_bindings");
        }
    }
}
