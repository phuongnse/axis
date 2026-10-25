using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    command = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: true),
                    os_user = table.Column<string>(type: "text", nullable: false),
                    machine = table.Column<string>(type: "text", nullable: false),
                    changed = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_records", x => x.id);
                });

            // Audit records are append-only: no statement may change or remove one, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION platform.reject_audit_record_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'platform.audit_records is append-only' USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER audit_records_no_change
                    BEFORE UPDATE OR DELETE ON platform.audit_records
                    FOR EACH ROW EXECUTE FUNCTION platform.reject_audit_record_change();

                CREATE TRIGGER audit_records_no_truncate
                    BEFORE TRUNCATE ON platform.audit_records
                    FOR EACH STATEMENT EXECUTE FUNCTION platform.reject_audit_record_change();
                """);

            // Default privileges may already grant SELECT on new tables to the platform reader role, whose name is not known here.
            migrationBuilder.Sql(
                """
                REVOKE ALL ON platform.audit_records FROM PUBLIC;
                DO $$
                DECLARE grantee_name text;
                BEGIN
                    FOR grantee_name IN
                        SELECT DISTINCT r.rolname FROM pg_class c
                        CROSS JOIN LATERAL aclexplode(c.relacl) a
                        JOIN pg_roles r ON r.oid = a.grantee
                        WHERE c.oid = 'platform.audit_records'::regclass AND a.grantee <> c.relowner
                    LOOP
                        EXECUTE format('REVOKE ALL ON platform.audit_records FROM %I', grantee_name);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER audit_records_no_truncate ON platform.audit_records;
                DROP TRIGGER audit_records_no_change ON platform.audit_records;
                DROP FUNCTION platform.reject_audit_record_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "platform");
        }
    }
}
