using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_assertion_replays",
                columns: table => new
                {
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_assertion_replays", x => x.digest);
                });

            migrationBuilder.CreateTable(
                name: "service_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    workspace_grant_status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_identities_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_identity_key_tombstones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    service_identity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_identity_key_tombstones", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_identity_key_tombstones_service_identities_service_~",
                        column: x => x.service_identity_id,
                        principalTable: "service_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_identity_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    x = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    y = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    service_identity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_identity_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_identity_keys_service_identities_service_identity_id",
                        column: x => x.service_identity_id,
                        principalTable: "service_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_assertion_replays_expires_at",
                table: "service_assertion_replays",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_service_identities_client_id",
                table: "service_identities",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_identities_workspace_id",
                table: "service_identities",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_identity_key_tombstones_service_identity_id_kid",
                table: "service_identity_key_tombstones",
                columns: new[] { "service_identity_id", "kid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_identity_key_tombstones_service_identity_id_thumbpr~",
                table: "service_identity_key_tombstones",
                columns: new[] { "service_identity_id", "thumbprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_identity_keys_service_identity_id_kid",
                table: "service_identity_keys",
                columns: new[] { "service_identity_id", "kid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_identity_keys_service_identity_id_thumbprint",
                table: "service_identity_keys",
                columns: new[] { "service_identity_id", "thumbprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_assertion_replays");

            migrationBuilder.DropTable(
                name: "service_identity_key_tombstones");

            migrationBuilder.DropTable(
                name: "service_identity_keys");

            migrationBuilder.DropTable(
                name: "service_identities");
        }
    }
}
