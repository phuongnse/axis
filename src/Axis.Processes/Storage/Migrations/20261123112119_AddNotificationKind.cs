using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "axis",
                table: "notifications",
                type: "text",
                nullable: false,
                defaultValue: "info");

            // A quota notice is a warning, as every other notification stored before reads as info.
            migrationBuilder.Sql("UPDATE axis.notifications SET kind = 'warning' WHERE source = 'quota'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                schema: "axis",
                table: "notifications");
        }
    }
}
