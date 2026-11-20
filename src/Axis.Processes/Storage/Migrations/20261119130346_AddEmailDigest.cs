using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "digest_user_id",
                schema: "axis",
                table: "email_outbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "link",
                schema: "axis",
                table: "email_outbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "process_instance_id",
                schema: "axis",
                table: "email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_digest_user_id_created_at",
                schema: "axis",
                table: "email_outbox",
                columns: new[] { "digest_user_id", "created_at" },
                filter: "digest_user_id IS NOT NULL AND state = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_outbox_digest_user_id_created_at",
                schema: "axis",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "digest_user_id",
                schema: "axis",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "link",
                schema: "axis",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "process_instance_id",
                schema: "axis",
                table: "email_outbox");
        }
    }
}
