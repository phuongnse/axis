using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddJobInput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddColumn<string>(
                name: "input",
                schema: "platform",
                table: "jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "input",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume')");
        }
    }
}
