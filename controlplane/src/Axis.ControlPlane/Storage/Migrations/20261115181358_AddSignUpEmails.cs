using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sign_up_emails",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_up_emails", x => x.id);
                    table.CheckConstraint("ck_sign_up_emails_address_hash", "octet_length(address_hash) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_up_emails_address_hash_queued_at",
                schema: "control_plane",
                table: "sign_up_emails",
                columns: new[] { "address_hash", "queued_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sign_up_emails",
                schema: "control_plane");
        }
    }
}
