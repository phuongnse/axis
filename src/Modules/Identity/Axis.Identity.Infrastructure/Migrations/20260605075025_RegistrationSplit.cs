using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accepted_privacy_version",
                table: "team_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_terms_version",
                table: "team_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "legal_accepted_at",
                table: "team_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "team_account_registration_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_account_registration_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_account_registration_tokens_team_accounts_Team_account~",
                        column: x => x.TeamAccountId,
                        principalTable: "team_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_account_registration_tokens_TeamAccountId_Purpose",
                table: "team_account_registration_tokens",
                columns: new[] { "TeamAccountId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_team_account_registration_tokens_TokenHash",
                table: "team_account_registration_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_account_registration_tokens");

            migrationBuilder.DropColumn(
                name: "accepted_privacy_version",
                table: "team_accounts");

            migrationBuilder.DropColumn(
                name: "accepted_terms_version",
                table: "team_accounts");

            migrationBuilder.DropColumn(
                name: "legal_accepted_at",
                table: "team_accounts");
        }
    }
}
