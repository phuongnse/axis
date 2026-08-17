using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.CheckConstraint("CK_identity_audit_outbox_scope", "actor_kind IN ('System', 'Anonymous') OR workspace_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictScopes", x => x.Id);
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
                name: "registration_idempotency",
                columns: table => new
                {
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_idempotency", x => x.IdempotencyKey);
                });

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
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    language_preference = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    theme_preference = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    accepted_terms_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    accepted_privacy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    legal_accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                        column: x => x.ApplicationId,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_terms_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    accepted_privacy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    legal_accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.id);
                    table.CheckConstraint("CK_workspaces_type_organization", "(type = 'Personal' AND organization_id IS NULL) OR (type = 'Organization' AND organization_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_workspaces_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_verification_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "service_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    workspace_grant_status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    terminal_material_purged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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
                name: "workspace_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_product_builder = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_by_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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
                name: "IX_email_verification_tokens_TokenHash",
                table: "email_verification_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_tokens_UserId",
                table: "email_verification_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_outbox_status_next_attempt_at",
                table: "identity_audit_outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_ClientId",
                table: "OpenIddictApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
                table: "OpenIddictAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictScopes_Name",
                table: "OpenIddictScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
                table: "OpenIddictTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_AuthorizationId",
                table: "OpenIddictTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ReferenceId",
                table: "OpenIddictTokens",
                column: "ReferenceId",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

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
                name: "IX_workspace_invitations_workspace_id_normalized_email",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "normalized_email" },
                unique: true,
                filter: "status = 'Pending' AND normalized_email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_status_created_at",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "status", "created_at" });

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

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_organization_id",
                table: "workspaces",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_slug",
                table: "workspaces",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "create_organization_idempotency");

            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "identity_audit_outbox");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens");

            migrationBuilder.DropTable(
                name: "organization_memberships");

            migrationBuilder.DropTable(
                name: "registration_idempotency");

            migrationBuilder.DropTable(
                name: "service_assertion_replays");

            migrationBuilder.DropTable(
                name: "service_identity_key_tombstones");

            migrationBuilder.DropTable(
                name: "service_identity_keys");

            migrationBuilder.DropTable(
                name: "workspace_context_transitions");

            migrationBuilder.DropTable(
                name: "workspace_invitation_handoffs");

            migrationBuilder.DropTable(
                name: "workspace_invitation_tokens");

            migrationBuilder.DropTable(
                name: "workspace_memberships");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations");

            migrationBuilder.DropTable(
                name: "service_identities");

            migrationBuilder.DropTable(
                name: "workspace_invitations");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
