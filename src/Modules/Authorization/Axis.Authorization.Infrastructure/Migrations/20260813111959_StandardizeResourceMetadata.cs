using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Authorization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeResourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "authorization_product_role_assignments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "authorization_product_role_assignments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "authorization_product_role_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "authorization_product_role_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "authorization_product_role_assignments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "authorization_product_role_assignments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "authorization_product_role_assignments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "authorization_product_role_assignments");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "authorization_product_role_assignments");
        }
    }
}
