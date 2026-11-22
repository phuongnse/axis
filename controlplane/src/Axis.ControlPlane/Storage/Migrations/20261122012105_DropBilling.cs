using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class DropBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D168 drops the billing tables and the subscription's currency. The currency trigger goes before its column.
            migrationBuilder.Sql("DROP TRIGGER subscriptions_currency_fixed ON control_plane.subscriptions; DROP FUNCTION control_plane.reject_subscription_currency_change();");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payment_links",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "webhook_inbox",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_currency",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropSequence(
                name: "payment_order_codes",
                schema: "billing");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions",
                sql: "period IS NULL OR plan_code IS NOT NULL");

            // EF never drops a schema or a trigger function itself. The ledger's triggers went with its table.
            migrationBuilder.Sql("DROP FUNCTION billing.reject_ledger_entry_change();");

            migrationBuilder.DropSchema(
                name: "billing");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateSequence(
                name: "payment_order_codes",
                schema: "billing",
                maxValue: 9007199254740991L);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "control_plane",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    currency = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    plan_code = table.Column<string>(type: "text", nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_currency", "currency IN ('VND', 'USD')");
                    table.CheckConstraint("ck_invoices_kind", "kind IN ('period', 'change')");
                    table.CheckConstraint("ck_invoices_period", "period IN ('month', 'year')");
                    table.CheckConstraint("ck_invoices_period_start", "(kind = 'period') = (period_start IS NOT NULL)");
                    table.CheckConstraint("ck_invoices_status", "status IN ('open', 'paid', 'void')");
                    table.ForeignKey(
                        name: "fk_invoices_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "control_plane",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    provider_reference = table.Column<string>(type: "text", nullable: true),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_sign", "(kind IN ('charge', 'refund') AND amount_minor > 0) OR (kind IN ('payment', 'credit') AND amount_minor < 0) OR (kind = 'proration' AND amount_minor <> 0)");
                    table.CheckConstraint("ck_ledger_entries_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_ledger_entries_kind", "kind IN ('charge', 'credit', 'payment', 'refund', 'proration')");
                });

            migrationBuilder.CreateTable(
                name: "webhook_inbox",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    order_code = table.Column<long>(type: "bigint", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_inbox", x => x.id);
                    table.CheckConstraint("ck_webhook_inbox_outcome", "outcome IN ('applied', 'outOfPlace')");
                    table.CheckConstraint("ck_webhook_inbox_provider", "provider IN ('payos')");
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => new { x.invoice_id, x.line_number });
                    table.CheckConstraint("ck_invoice_lines_kind", "kind IN ('plan', 'addOn')");
                    table.CheckConstraint("ck_invoice_lines_line_number", "line_number >= 1");
                    table.CheckConstraint("ck_invoice_lines_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_links",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_name = table.Column<string>(type: "text", nullable: true),
                    account_number = table.Column<string>(type: "text", nullable: true),
                    bin = table.Column<string>(type: "text", nullable: true),
                    checkout_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    order_code = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('billing.payment_order_codes')"),
                    payment_link_id = table.Column<string>(type: "text", nullable: true),
                    qr_code = table.Column<string>(type: "text", nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_links", x => x.invoice_id);
                    table.CheckConstraint("ck_payment_links_created", "(created_at IS NULL AND payment_link_id IS NULL AND checkout_url IS NULL AND qr_code IS NULL AND bin IS NULL AND account_number IS NULL AND account_name IS NULL AND description IS NULL) OR (created_at IS NOT NULL AND payment_link_id IS NOT NULL AND checkout_url IS NOT NULL AND qr_code IS NOT NULL AND bin IS NOT NULL AND account_number IS NOT NULL AND account_name IS NOT NULL AND description IS NOT NULL)");
                    table.CheckConstraint("ck_payment_links_order_code", "order_code BETWEEN 1 AND 9007199254740991");
                    table.ForeignKey(
                        name: "fk_payment_links_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_currency",
                schema: "control_plane",
                table: "subscriptions",
                sql: "currency IN ('VND', 'USD')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions",
                sql: "(currency IS NULL) = (period IS NULL) AND (currency IS NULL OR plan_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_subscription_id",
                schema: "billing",
                table: "invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_period",
                schema: "billing",
                table: "invoices",
                columns: new[] { "subscription_id", "period_start" },
                unique: true,
                filter: "kind = 'period'");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_invoice_id",
                schema: "billing",
                table: "ledger_entries",
                column: "invoice_id");

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

            migrationBuilder.CreateIndex(
                name: "ux_payment_links_order_code",
                schema: "billing",
                table: "payment_links",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_inbox_pending",
                schema: "billing",
                table: "webhook_inbox",
                columns: new[] { "occurred_at", "received_at", "id" },
                filter: "outcome IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_inbox_event",
                schema: "billing",
                table: "webhook_inbox",
                columns: new[] { "provider", "event_id" },
                unique: true);

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

            migrationBuilder.Sql(
                """
                CREATE FUNCTION control_plane.reject_subscription_currency_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM billing.ledger_entries WHERE subscription_id = OLD.id) THEN
                        RAISE EXCEPTION 'the currency of subscription % is fixed once it has a ledger entry', OLD.id USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER subscriptions_currency_fixed
                    BEFORE UPDATE OF currency ON control_plane.subscriptions
                    FOR EACH ROW WHEN (OLD.currency IS DISTINCT FROM NEW.currency)
                    EXECUTE FUNCTION control_plane.reject_subscription_currency_change();
                """);
        }
    }
}
