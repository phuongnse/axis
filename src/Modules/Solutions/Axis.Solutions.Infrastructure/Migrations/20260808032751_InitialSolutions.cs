using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Solutions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solution_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    package_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    envelope = table.Column<byte[]>(type: "bytea", nullable: false),
                    axis_openapi_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    publisher_key_id = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    source_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    build_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "solutions_audit_outbox",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    originating_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    event_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solution_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    problem_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solutions_audit_outbox", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "trusted_publisher_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    key_id = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    spki_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key_pem = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    configuration_revision = table.Column<long>(type: "bigint", nullable: false),
                    is_tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trusted_publisher_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trusted_publisher_ledger_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    active_revision = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trusted_publisher_ledger_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "solution_components",
                columns: table => new
                {
                    solution_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    component_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    component_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    depends_on = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_components", x => new { x.solution_version_id, x.component_type, x.component_key });
                    table.ForeignKey(
                        name: "FK_solution_components_solution_versions_solution_version_id",
                        column: x => x.solution_version_id,
                        principalTable: "solution_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "solution_installations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provisioning_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    compliance_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_installations", x => x.id);
                    table.ForeignKey(
                        name: "FK_solution_installations_solution_versions_solution_version_id",
                        column: x => x.solution_version_id,
                        principalTable: "solution_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solution_installation_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lease_epoch = table.Column<long>(type: "bigint", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    problem_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_installation_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_solution_installation_operations_solution_installations_ins~",
                        column: x => x.installation_id,
                        principalTable: "solution_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "solution_installation_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    component_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    component_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    component_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    applying_epoch = table.Column<long>(type: "bigint", nullable: false),
                    reclaimed_epoch = table.Column<long>(type: "bigint", nullable: true),
                    problem_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_installation_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_solution_installation_steps_solution_installation_operation~",
                        column: x => x.operation_id,
                        principalTable: "solution_installation_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_solution_installation_operations_installation_id",
                table: "solution_installation_operations",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "IX_solution_installation_operations_workspace_id_idempotency_k~",
                table: "solution_installation_operations",
                columns: new[] { "workspace_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solution_installation_steps_operation_id_step_order",
                table: "solution_installation_steps",
                columns: new[] { "operation_id", "step_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solution_installations_solution_version_id",
                table: "solution_installations",
                column: "solution_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_solution_installations_workspace_id_solution_version_id",
                table: "solution_installations",
                columns: new[] { "workspace_id", "solution_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solution_versions_solution_key_version",
                table: "solution_versions",
                columns: new[] { "solution_key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solution_versions_solution_key_version_package_sha256",
                table: "solution_versions",
                columns: new[] { "solution_key", "version", "package_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solutions_audit_outbox_status_next_attempt_at",
                table: "solutions_audit_outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_trusted_publisher_keys_publisher_id_key_id",
                table: "trusted_publisher_keys",
                columns: new[] { "publisher_id", "key_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solution_components");

            migrationBuilder.DropTable(
                name: "solution_installation_steps");

            migrationBuilder.DropTable(
                name: "solutions_audit_outbox");

            migrationBuilder.DropTable(
                name: "trusted_publisher_keys");

            migrationBuilder.DropTable(
                name: "trusted_publisher_ledger_state");

            migrationBuilder.DropTable(
                name: "solution_installation_operations");

            migrationBuilder.DropTable(
                name: "solution_installations");

            migrationBuilder.DropTable(
                name: "solution_versions");
        }
    }
}
