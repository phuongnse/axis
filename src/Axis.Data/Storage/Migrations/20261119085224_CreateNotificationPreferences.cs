using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "axis",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    sources = table.Column<string>(type: "jsonb", nullable: false),
                    digest_hour = table.Column<short>(type: "smallint", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "axis");
        }
    }
}
