using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRequestOutput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "output",
                schema: "axis",
                table: "publish_requests",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "output",
                schema: "axis",
                table: "publish_requests");
        }
    }
}
