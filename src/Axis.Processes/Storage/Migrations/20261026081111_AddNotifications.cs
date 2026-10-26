using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_reads",
                schema: "axis",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_reads", x => new { x.notification_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_kind = table.Column<string>(type: "text", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_key = table.Column<string>(type: "text", nullable: false),
                    body_key = table.Column<string>(type: "text", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    open_page = table.Column<string>(type: "text", nullable: true),
                    open_parameters = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => new { x.id, x.recipient_kind, x.recipient });
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_application_id_recipient_created_at",
                schema: "axis",
                table: "notifications",
                columns: new[] { "application_id", "recipient_kind", "recipient", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_reads",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "axis");
        }
    }
}
