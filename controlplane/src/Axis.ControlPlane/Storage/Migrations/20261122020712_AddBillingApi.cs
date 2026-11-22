using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_records_actor_kind",
                schema: "control_plane",
                table: "audit_records");

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_through",
                schema: "control_plane",
                table: "subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_requests",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_requests", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_records_actor_kind",
                schema: "control_plane",
                table: "audit_records",
                sql: "actor_kind IN ('account', 'webhook', 'job', 'operator', 'billing')");

            migrationBuilder.CreateIndex(
                name: "ix_billing_requests_idempotency_key_created_at",
                schema: "control_plane",
                table: "billing_requests",
                columns: new[] { "idempotency_key", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_requests",
                schema: "control_plane");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_records_actor_kind",
                schema: "control_plane",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "paid_through",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_records_actor_kind",
                schema: "control_plane",
                table: "audit_records",
                sql: "actor_kind IN ('account', 'webhook', 'job', 'operator')");
        }
    }
}
