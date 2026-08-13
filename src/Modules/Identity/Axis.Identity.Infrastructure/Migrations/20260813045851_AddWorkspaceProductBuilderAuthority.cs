using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceProductBuilderAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_product_builder",
                table: "workspace_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE workspace_memberships AS membership
                SET is_product_builder = TRUE
                FROM workspaces AS workspace
                WHERE membership.workspace_id = workspace.id
                  AND membership.status <> 'Removed'
                  AND (
                    (workspace.type = 'Personal' AND membership.role = 'Owner')
                    OR (
                      workspace.type = 'Organization'
                      AND membership.role = 'Administrator'
                      AND EXISTS (
                        SELECT 1
                        FROM organization_memberships AS organization_membership
                        WHERE organization_membership.organization_id = workspace.organization_id
                          AND organization_membership.user_id = membership.user_id
                          AND organization_membership.role = 'Owner'
                      )
                    )
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_product_builder",
                table: "workspace_memberships");
        }
    }
}
