using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsAndAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "control_plane");

            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_kind = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_records", x => x.id);
                    table.CheckConstraint("ck_audit_records_actor_kind", "actor_kind IN ('account', 'webhook', 'job', 'operator')");
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false, defaultValue: "queued"),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "text", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_state", "state IN ('queued', 'running', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_next_attempt_at",
                schema: "control_plane",
                table: "jobs",
                column: "next_attempt_at",
                filter: "state IN ('queued', 'running')");

            // Audit records are append-only: no statement may change or remove one, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION control_plane.reject_audit_record_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'control_plane.audit_records is append-only' USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER audit_records_no_change
                    BEFORE UPDATE OR DELETE ON control_plane.audit_records
                    FOR EACH ROW EXECUTE FUNCTION control_plane.reject_audit_record_change();

                CREATE TRIGGER audit_records_no_truncate
                    BEFORE TRUNCATE ON control_plane.audit_records
                    FOR EACH STATEMENT EXECUTE FUNCTION control_plane.reject_audit_record_change();

                REVOKE ALL ON control_plane.audit_records FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER audit_records_no_truncate ON control_plane.audit_records;
                DROP TRIGGER audit_records_no_change ON control_plane.audit_records;
                DROP FUNCTION control_plane.reject_audit_record_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "control_plane");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "control_plane");
        }
    }
}
