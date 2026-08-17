using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Solutions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeResourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "solution_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "solution_versions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "solution_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "solution_installations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "solution_installations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "solution_installations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "solution_installations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "solution_installations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "solution_installations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "solution_versions");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "solution_versions");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "solution_versions");

            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "solution_installations");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "solution_installations");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "solution_installations");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "solution_installations");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "solution_installations");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "solution_installations");
        }
    }
}
