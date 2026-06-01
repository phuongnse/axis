using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLegalAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accepted_privacy_version",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_terms_version",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "legal_accepted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accepted_privacy_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "accepted_terms_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "legal_accepted_at",
                table: "users");
        }
    }
}
