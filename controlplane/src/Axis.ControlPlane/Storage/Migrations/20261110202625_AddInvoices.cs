using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    currency = table.Column<string>(type: "text", nullable: false),
                    plan_code = table.Column<string>(type: "text", nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
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
                name: "invoice_lines",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_invoice_id",
                schema: "billing",
                table: "ledger_entries",
                column: "invoice_id");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_invoice_id",
                schema: "billing",
                table: "ledger_entries");
        }
    }
}
