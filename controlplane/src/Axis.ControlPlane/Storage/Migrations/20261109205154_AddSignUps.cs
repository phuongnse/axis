using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sign_ups",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    business_name = table.Column<string>(type: "text", nullable: false),
                    subdomain = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    application = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_ups", x => x.id);
                    table.CheckConstraint("ck_sign_ups_language", "language IN ('en', 'vi')");
                    table.CheckConstraint("ck_sign_ups_state", "state IN ('pending', 'verified', 'expired')");
                    table.CheckConstraint("ck_sign_ups_token_hash", "token_hash IS NULL OR octet_length(token_hash) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_ups_expires_at",
                schema: "control_plane",
                table: "sign_ups",
                column: "expires_at",
                filter: "state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ux_sign_ups_pending_subdomain",
                schema: "control_plane",
                table: "sign_ups",
                column: "subdomain",
                unique: true,
                filter: "state = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sign_ups",
                schema: "control_plane");
        }
    }
}
