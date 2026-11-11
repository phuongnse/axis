using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoringRequestRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "refresh_token",
                schema: "axis",
                table: "authoring_requests",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refresh_token",
                schema: "axis",
                table: "authoring_requests");
        }
    }
}
