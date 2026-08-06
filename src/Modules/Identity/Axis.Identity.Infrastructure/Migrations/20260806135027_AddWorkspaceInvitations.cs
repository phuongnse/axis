using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    requested_role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    terminal_material_purged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_invitations_users_inviter_user_id",
                        column: x => x.inviter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_invitations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitation_handoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_generation = table.Column<int>(type: "integer", nullable: false),
                    handoff_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_invitation_handoffs", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_invitation_handoffs_workspace_invitations_invitat~",
                        column: x => x.invitation_id,
                        principalTable: "workspace_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitation_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    delivery_envelope = table.Column<string>(type: "text", nullable: true),
                    delivery_correlation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    delivery_status = table.Column<string>(type: "text", nullable: false),
                    delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_delivery_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_delivery_error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_invitation_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_invitation_tokens_workspace_invitations_invitatio~",
                        column: x => x.invitation_id,
                        principalTable: "workspace_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_handoffs_handoff_hash",
                table: "workspace_invitation_handoffs",
                column: "handoff_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_handoffs_invitation_id",
                table: "workspace_invitation_handoffs",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_handoffs_status_expires_at",
                table: "workspace_invitation_handoffs",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_tokens_delivery_status_next_delivery_a~",
                table: "workspace_invitation_tokens",
                columns: new[] { "delivery_status", "next_delivery_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_tokens_invitation_id_generation",
                table: "workspace_invitation_tokens",
                columns: new[] { "invitation_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitation_tokens_token_hash",
                table: "workspace_invitation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_inviter_user_id",
                table: "workspace_invitations",
                column: "inviter_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_organization_id",
                table: "workspace_invitations",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email_request~",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "normalized_email", "requested_role" },
                unique: true,
                filter: "status = 'Pending' AND normalized_email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_status_created_at",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_invitation_handoffs");

            migrationBuilder.DropTable(
                name: "workspace_invitation_tokens");

            migrationBuilder.DropTable(
                name: "workspace_invitations");
        }
    }
}
