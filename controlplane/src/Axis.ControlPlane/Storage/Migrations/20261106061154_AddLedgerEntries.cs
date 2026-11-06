using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    provider_reference = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_sign", "(kind IN ('charge', 'refund') AND amount_minor > 0) OR (kind IN ('payment', 'credit') AND amount_minor < 0) OR (kind = 'proration' AND amount_minor <> 0)");
                    table.CheckConstraint("ck_ledger_entries_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_ledger_entries_kind", "kind IN ('charge', 'credit', 'payment', 'refund', 'proration')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_subscription_id",
                schema: "billing",
                table: "ledger_entries",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_idempotency_key",
                schema: "billing",
                table: "ledger_entries",
                column: "idempotency_key",
                unique: true);

            // Ledger entries are append-only: no statement may change or remove one, raw SQL included. The role that runs the
            // migrations owns the table, so it keeps only SELECT and INSERT. The triggers come first, as the revoke drops TRIGGER.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION billing.reject_ledger_entry_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'billing.ledger_entries is append-only' USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER ledger_entries_no_change
                    BEFORE UPDATE OR DELETE ON billing.ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION billing.reject_ledger_entry_change();

                CREATE TRIGGER ledger_entries_no_truncate
                    BEFORE TRUNCATE ON billing.ledger_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION billing.reject_ledger_entry_change();

                REVOKE ALL ON billing.ledger_entries FROM PUBLIC;
                REVOKE ALL ON billing.ledger_entries FROM CURRENT_USER;
                GRANT SELECT, INSERT ON billing.ledger_entries TO CURRENT_USER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER ledger_entries_no_truncate ON billing.ledger_entries;
                DROP TRIGGER ledger_entries_no_change ON billing.ledger_entries;
                DROP FUNCTION billing.reject_ledger_entry_change();
                """);

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "billing");
        }
    }
}
