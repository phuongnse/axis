using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "rule_definitions",
                newName: "updated_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "rule_definitions",
                newName: "created_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "archived_by_user_id",
                table: "rule_definitions",
                newName: "archived_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "published_by_user_id",
                table: "rule_definition_versions",
                newName: "published_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "rule_bindings",
                newName: "updated_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "rule_bindings",
                newName: "created_by_subject_id");

            migrationBuilder.AddColumn<string>(
                name: "archived_by_subject_kind",
                table: "rule_definitions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_subject_kind",
                table: "rule_definitions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_subject_kind",
                table: "rule_definitions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_by_subject_kind",
                table: "rule_definition_versions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_subject_kind",
                table: "rule_bindings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_subject_kind",
                table: "rule_bindings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE rule_definitions
                SET created_by_subject_kind = 'Human',
                    updated_by_subject_kind = 'Human',
                    archived_by_subject_kind = CASE
                        WHEN archived_by_subject_id IS NULL THEN NULL
                        ELSE 'Human'
                    END;

                UPDATE rule_definition_versions
                SET published_by_subject_kind = 'Human';

                UPDATE rule_bindings
                SET created_by_subject_kind = 'Human',
                    updated_by_subject_kind = 'Human',
                    revision_history = COALESCE(
                        (
                            SELECT jsonb_agg(
                                (entry - 'updatedByUserId') || jsonb_build_object(
                                    'updatedBySubjectKind', 0,
                                    'updatedBySubjectId', entry -> 'updatedByUserId')
                                ORDER BY ordinal_position)
                            FROM jsonb_array_elements(revision_history) WITH ORDINALITY AS revisions(entry, ordinal_position)
                        ),
                        '[]'::jsonb);
                """);

            SetRequired(migrationBuilder, "rule_definitions", "created_by_subject_kind");
            SetRequired(migrationBuilder, "rule_definitions", "updated_by_subject_kind");
            SetRequired(migrationBuilder, "rule_definition_versions", "published_by_subject_kind");
            SetRequired(migrationBuilder, "rule_bindings", "created_by_subject_kind");
            SetRequired(migrationBuilder, "rule_bindings", "updated_by_subject_kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM rule_definitions
                        WHERE created_by_subject_kind <> 'Human'
                           OR updated_by_subject_kind <> 'Human'
                           OR (archived_by_subject_kind IS NOT NULL AND archived_by_subject_kind <> 'Human'))
                       OR EXISTS (
                        SELECT 1 FROM rule_definition_versions
                        WHERE published_by_subject_kind <> 'Human')
                       OR EXISTS (
                        SELECT 1 FROM rule_bindings
                        WHERE created_by_subject_kind <> 'Human'
                           OR updated_by_subject_kind <> 'Human'
                           OR EXISTS (
                               SELECT 1
                               FROM jsonb_array_elements(revision_history) AS revisions(entry)
                               WHERE entry ->> 'updatedBySubjectKind' <> '0'))
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade Rules subject columns while Service subject data exists.';
                    END IF;
                END $$;

                UPDATE rule_bindings
                SET revision_history = COALESCE(
                    (
                        SELECT jsonb_agg(
                            (entry - 'updatedBySubjectKind' - 'updatedBySubjectId') ||
                            jsonb_build_object('updatedByUserId', entry -> 'updatedBySubjectId')
                            ORDER BY ordinal_position)
                        FROM jsonb_array_elements(revision_history) WITH ORDINALITY AS revisions(entry, ordinal_position)
                    ),
                    '[]'::jsonb);
                """);

            migrationBuilder.DropColumn(
                name: "archived_by_subject_kind",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "created_by_subject_kind",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_kind",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "published_by_subject_kind",
                table: "rule_definition_versions");

            migrationBuilder.DropColumn(
                name: "created_by_subject_kind",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_kind",
                table: "rule_bindings");

            migrationBuilder.RenameColumn(
                name: "updated_by_subject_id",
                table: "rule_definitions",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "created_by_subject_id",
                table: "rule_definitions",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "archived_by_subject_id",
                table: "rule_definitions",
                newName: "archived_by_user_id");

            migrationBuilder.RenameColumn(
                name: "published_by_subject_id",
                table: "rule_definition_versions",
                newName: "published_by_user_id");

            migrationBuilder.RenameColumn(
                name: "updated_by_subject_id",
                table: "rule_bindings",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "created_by_subject_id",
                table: "rule_bindings",
                newName: "created_by_user_id");
        }

        private static void SetRequired(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.AlterColumn<string>(
                name: column,
                table: table,
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);
        }
    }
}
