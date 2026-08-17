using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeResourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_actor_display_name",
                table: "rule_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_actor_kind",
                table: "rule_definitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_actor_subject_id",
                table: "rule_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_actor_display_name",
                table: "rule_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_actor_kind",
                table: "rule_definitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_actor_subject_id",
                table: "rule_definitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_actor_display_name",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "created_by_actor_kind",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "created_by_actor_subject_id",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_actor_display_name",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_actor_kind",
                table: "rule_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_actor_subject_id",
                table: "rule_definitions");
        }
    }
}
