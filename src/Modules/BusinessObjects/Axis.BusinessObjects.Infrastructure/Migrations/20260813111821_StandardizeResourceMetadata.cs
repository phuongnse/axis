using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeResourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "business_object_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "business_object_definitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "business_object_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "business_object_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "business_object_definitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "business_object_definitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "business_object_definitions");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "business_object_definitions");
        }
    }
}
