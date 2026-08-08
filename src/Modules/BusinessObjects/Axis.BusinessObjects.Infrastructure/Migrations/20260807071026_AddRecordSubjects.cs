using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "business_object_records",
                newName: "updated_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "business_object_records",
                newName: "created_by_subject_id");

            migrationBuilder.RenameColumn(
                name: "submitted_by_user_id",
                table: "business_object_records",
                newName: "submitted_by_subject_id_legacy");

            migrationBuilder.RenameColumn(
                name: "published_by_user_id",
                table: "business_object_definition_versions",
                newName: "published_by_subject_id");

            migrationBuilder.AddColumn<string>(
                name: "created_by_subject_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "business_object_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submitted_by_subject",
                table: "business_object_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_subject_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_by_subject_kind",
                table: "business_object_definition_versions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE business_object_records
                SET created_by_subject_kind = 'Human',
                    updated_by_subject_kind = 'Human',
                    owner_kind = 'Human',
                    owner_id = created_by_subject_id,
                    submitted_by_subject = CASE
                        WHEN submitted_by_subject_id_legacy IS NULL THEN NULL
                        ELSE 'Human:' || submitted_by_subject_id_legacy::text
                    END;

                UPDATE business_object_definition_versions
                SET published_by_subject_kind = 'Human';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "created_by_subject_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_id",
                table: "business_object_records",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "updated_by_subject_kind",
                table: "business_object_records",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "published_by_subject_kind",
                table: "business_object_definition_versions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "submitted_by_subject_id_legacy",
                table: "business_object_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM business_object_records
                        WHERE created_by_subject_kind <> 'Human'
                           OR updated_by_subject_kind <> 'Human'
                           OR owner_kind <> 'Human'
                           OR (submitted_by_subject IS NOT NULL AND submitted_by_subject NOT LIKE 'Human:%')
                    ) OR EXISTS (
                        SELECT 1
                        FROM business_object_definition_versions
                        WHERE published_by_subject_kind <> 'Human'
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade discriminated Business Object subjects containing Service authority.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by_user_id",
                table: "business_object_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE business_object_records
                SET submitted_by_user_id = CASE
                    WHEN submitted_by_subject IS NULL THEN NULL
                    ELSE split_part(submitted_by_subject, ':', 2)::uuid
                END;
                """);

            migrationBuilder.DropColumn(
                name: "created_by_subject_kind",
                table: "business_object_records");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "business_object_records");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "business_object_records");

            migrationBuilder.DropColumn(
                name: "submitted_by_subject",
                table: "business_object_records");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_kind",
                table: "business_object_records");

            migrationBuilder.DropColumn(
                name: "published_by_subject_kind",
                table: "business_object_definition_versions");

            migrationBuilder.RenameColumn(
                name: "updated_by_subject_id",
                table: "business_object_records",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "created_by_subject_id",
                table: "business_object_records",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "published_by_subject_id",
                table: "business_object_definition_versions",
                newName: "published_by_user_id");
        }
    }
}
