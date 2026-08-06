using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityGovernance : Migration
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
                        FROM "Workspaces"
                        WHERE type <> 'Personal') THEN
                        RAISE EXCEPTION 'Identity governance migration requires every legacy workspace to be Personal.'
                            USING ERRCODE = '23514';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Workspaces"
                        WHERE owner_user_id IS NULL) THEN
                        RAISE EXCEPTION 'Identity governance migration requires every legacy workspace to have an owner user.'
                            USING ERRCODE = '23514';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Workspaces" AS w
                        LEFT JOIN users AS u ON u.id = w.owner_user_id
                        WHERE u.id IS NULL) THEN
                        RAISE EXCEPTION 'Identity governance migration found a legacy workspace owner without a user.'
                            USING ERRCODE = '23503';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Workspaces" AS w
                        JOIN users AS u ON u.id = w.owner_user_id
                        WHERE lower(w.owner_email) <> lower(u.email)) THEN
                        RAISE EXCEPTION 'Identity governance migration found a legacy workspace owner email mismatch.'
                            USING ERRCODE = '23514';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Workspaces"
                        WHERE char_length(btrim(name)) NOT BETWEEN 2 AND 100) THEN
                        RAISE EXCEPTION 'Identity governance migration found a workspace name outside the supported length.'
                            USING ERRCODE = '22001';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_Workspaces",
                table: "Workspaces");

            migrationBuilder.RenameTable(
                name: "Workspaces",
                newName: "workspaces");

            migrationBuilder.RenameIndex(
                name: "IX_Workspaces_slug",
                table: "workspaces",
                newName: "IX_workspaces_slug");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "workspaces",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                table: "workspaces",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "workspaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_workspaces",
                table: "workspaces",
                column: "id");

            migrationBuilder.CreateTable(
                name: "create_organization_idempotency",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    canonical_request = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_create_organization_idempotency", x => x.idempotency_key);
                });

            migrationBuilder.CreateTable(
                name: "identity_audit_outbox",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_identity_audit_outbox", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_context_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_audit_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_correlation_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_correlation_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    terminal_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    audit_projection_confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    redis_cleanup_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_context_transitions", x => x.id);
                    table.CheckConstraint("CK_transition_source_digest", "source_correlation_digest ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_transition_target_digest", "target_correlation_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_workspace_context_transitions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_context_transitions_workspaces_source_workspace_id",
                        column: x => x.source_workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_context_transitions_workspaces_target_workspace_id",
                        column: x => x.target_workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_memberships_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO workspace_memberships (
                    id,
                    workspace_id,
                    user_id,
                    role,
                    status,
                    revision)
                SELECT
                    gen_random_uuid(),
                    id,
                    owner_user_id,
                    'Owner',
                    'Active',
                    1
                FROM workspaces;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM workspaces AS w
                        LEFT JOIN workspace_memberships AS m
                            ON m.workspace_id = w.id
                            AND m.user_id = w.owner_user_id
                            AND m.role = 'Owner'
                            AND m.status = 'Active'
                            AND m.revision = 1
                        WHERE m.id IS NULL) THEN
                        RAISE EXCEPTION 'Identity governance migration failed to backfill every personal workspace owner.'
                            USING ERRCODE = '23514';
                    END IF;

                    IF EXISTS (
                        SELECT workspace_id
                        FROM workspace_memberships
                        WHERE role = 'Owner' AND status = 'Active'
                        GROUP BY workspace_id
                        HAVING count(*) <> 1) THEN
                        RAISE EXCEPTION 'Identity governance migration produced an invalid personal workspace owner count.'
                            USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_owner_user_id_type",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "owner_email",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "workspaces");

            migrationBuilder.CreateTable(
                name: "organization_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_organization_memberships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_organization_id",
                table: "workspaces",
                column: "organization_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_workspaces_type_organization",
                table: "workspaces",
                sql: "(type = 'Personal' AND organization_id IS NULL) OR (type = 'Organization' AND organization_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_outbox_status_next_attempt_at",
                table: "identity_audit_outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_organization_id_user_id",
                table: "organization_memberships",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_user_id_status",
                table: "organization_memberships",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_source_correlation_digest",
                table: "workspace_context_transitions",
                column: "source_correlation_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_source_workspace_id",
                table: "workspace_context_transitions",
                column: "source_workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_status_expires_at",
                table: "workspace_context_transitions",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_target_correlation_digest",
                table: "workspace_context_transitions",
                column: "target_correlation_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_target_workspace_id",
                table: "workspace_context_transitions",
                column: "target_workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_terminal_audit_event_id",
                table: "workspace_context_transitions",
                column: "terminal_audit_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_context_transitions_user_id",
                table: "workspace_context_transitions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memberships_user_id_status",
                table: "workspace_memberships",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memberships_workspace_id_role",
                table: "workspace_memberships",
                columns: new[] { "workspace_id", "role" },
                unique: true,
                filter: "role = 'Owner' AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memberships_workspace_id_user_id",
                table: "workspace_memberships",
                columns: new[] { "workspace_id", "user_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workspaces_organizations_organization_id",
                table: "workspaces",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Identity governance is a clean cutover. Roll back with a forward fix or reviewed database restore.");
        }
    }
}
