using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "axis",
                table: "notifications",
                type: "text",
                nullable: false,
                defaultValue: "process");

            // Every notification with no application is a quota notice to the administrators.
            migrationBuilder.Sql("UPDATE axis.notifications SET source = 'quota' WHERE application_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source",
                schema: "axis",
                table: "notifications");
        }
    }
}
