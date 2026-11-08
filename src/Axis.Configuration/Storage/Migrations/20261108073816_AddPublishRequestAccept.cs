using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRequestAccept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client",
                schema: "axis",
                table: "publish_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "draft_id",
                schema: "axis",
                table: "publish_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "folder",
                schema: "axis",
                table: "publish_requests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "client",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "draft_id",
                schema: "axis",
                table: "publish_requests");

            migrationBuilder.DropColumn(
                name: "folder",
                schema: "axis",
                table: "publish_requests");
        }
    }
}
