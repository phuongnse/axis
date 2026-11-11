using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_inbox",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    event_id = table.Column<string>(type: "text", nullable: false),
                    order_code = table.Column<long>(type: "bigint", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_inbox", x => x.id);
                    table.CheckConstraint("ck_webhook_inbox_outcome", "outcome IN ('applied', 'outOfPlace')");
                    table.CheckConstraint("ck_webhook_inbox_provider", "provider IN ('payos')");
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_inbox",
                schema: "billing");
        }
    }
}
