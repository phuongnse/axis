using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Authorization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authorization_audit_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    read_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_audit_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authorization_idempotency",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_idempotency", x => new { x.workspace_id, x.idempotency_key });
                });

            migrationBuilder.CreateTable(
                name: "authorization_installed_policies",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    canonical_content = table.Column<string>(type: "text", nullable: false),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_installed_policies", x => new { x.workspace_id, x.version_id });
                });

            migrationBuilder.CreateTable(
                name: "authorization_product_role_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_product_role_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authorization_audit_outbox_delivery_state_next_attempt_at",
                table: "authorization_audit_outbox",
                columns: new[] { "delivery_state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_authorization_idempotency_assignment_id",
                table: "authorization_idempotency",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_product_role_assignments_workspace_id_subject~",
                table: "authorization_product_role_assignments",
                columns: new[] { "workspace_id", "subject_kind", "subject_id", "policy_version_id", "role_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorization_audit_outbox");

            migrationBuilder.DropTable(
                name: "authorization_idempotency");

            migrationBuilder.DropTable(
                name: "authorization_installed_policies");

            migrationBuilder.DropTable(
                name: "authorization_product_role_assignments");
        }
    }
}
