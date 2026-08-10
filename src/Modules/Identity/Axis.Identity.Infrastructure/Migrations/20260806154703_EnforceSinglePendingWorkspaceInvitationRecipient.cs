using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSinglePendingWorkspaceInvitationRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM workspace_invitations
                        WHERE status = 'Pending'
                            AND normalized_email IS NOT NULL
                        GROUP BY workspace_id, normalized_email
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot enforce one pending workspace invitation per recipient: conflicting pending invitations exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email_request~",
                table: "workspace_invitations");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "normalized_email" },
                unique: true,
                filter: "status = 'Pending' AND normalized_email IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email",
                table: "workspace_invitations");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email_request~",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "normalized_email", "requested_role" },
                unique: true,
                filter: "status = 'Pending' AND normalized_email IS NOT NULL");
        }
    }
}
