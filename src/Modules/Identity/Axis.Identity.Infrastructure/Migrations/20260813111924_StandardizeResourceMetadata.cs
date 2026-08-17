using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeResourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "workspace_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "workspace_memberships",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "workspace_memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "workspace_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "workspace_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "workspace_memberships",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "workspace_memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "workspace_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "workspace_invitations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "workspace_invitations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "workspace_invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "workspace_invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "workspace_invitations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "workspace_invitations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "workspace_invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "service_identities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_kind",
                table: "service_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_subject_id",
                table: "service_identities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "service_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_display_name",
                table: "service_identities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by_kind",
                table: "service_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_subject_id",
                table: "service_identities",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "workspace_invitations");

            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "created_by_kind",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "created_by_subject_id",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "updated_by_display_name",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "updated_by_kind",
                table: "service_identities");

            migrationBuilder.DropColumn(
                name: "updated_by_subject_id",
                table: "service_identities");
        }
    }
}
