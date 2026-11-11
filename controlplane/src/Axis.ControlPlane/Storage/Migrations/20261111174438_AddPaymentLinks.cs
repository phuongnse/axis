using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "payment_order_codes",
                schema: "billing",
                maxValue: 9007199254740991L);

            migrationBuilder.CreateTable(
                name: "payment_links",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_code = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('billing.payment_order_codes')"),
                    payment_link_id = table.Column<string>(type: "text", nullable: true),
                    checkout_url = table.Column<string>(type: "text", nullable: true),
                    qr_code = table.Column<string>(type: "text", nullable: true),
                    bin = table.Column<string>(type: "text", nullable: true),
                    account_number = table.Column<string>(type: "text", nullable: true),
                    account_name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "ux_payment_links_order_code",
                schema: "billing",
                table: "payment_links",
                column: "order_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_links",
                schema: "billing");

            migrationBuilder.DropSequence(
                name: "payment_order_codes",
                schema: "billing");
        }
    }
}
