using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorCallLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lookups",
                schema: "axis",
                table: "connector_calls",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lookups",
                schema: "axis",
                table: "connector_calls");
        }
    }
}
