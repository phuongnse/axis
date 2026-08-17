using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.id);
                    table.CheckConstraint("CK_audit_records_actor", "(actor_kind IN ('Human', 'ServiceIdentity') AND actor_id IS NOT NULL) OR (actor_kind IN ('System', 'Anonymous') AND actor_id IS NULL)");
                    table.CheckConstraint("CK_audit_records_scope", "actor_kind IN ('System', 'Anonymous') OR workspace_id IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_event_id",
                table: "audit_records",
                column: "event_id",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION prevent_audit_record_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Audit records are append-only.' USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER audit_records_append_only
                BEFORE UPDATE OR DELETE ON audit_records
                FOR EACH ROW
                EXECUTE FUNCTION prevent_audit_record_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records");

            migrationBuilder.Sql("DROP FUNCTION prevent_audit_record_mutation();");
        }
    }
}
