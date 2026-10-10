using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    process_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_entity_id_record_id_occurred_at_id",
                schema: "axis",
                table: "audit_records",
                columns: new[] { "entity_id", "record_id", "occurred_at", "id" },
                descending: new[] { false, false, true, true });

            // Audit records are append-only: no statement may change or remove one, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION axis.reject_audit_record_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'axis.audit_records is append-only' USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER audit_records_no_change
                    BEFORE UPDATE OR DELETE ON axis.audit_records
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_audit_record_change();

                CREATE TRIGGER audit_records_no_truncate
                    BEFORE TRUNCATE ON axis.audit_records
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_audit_record_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER audit_records_no_truncate ON axis.audit_records;
                DROP TRIGGER audit_records_no_change ON axis.audit_records;
                DROP FUNCTION axis.reject_audit_record_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "axis");
        }
    }
}
