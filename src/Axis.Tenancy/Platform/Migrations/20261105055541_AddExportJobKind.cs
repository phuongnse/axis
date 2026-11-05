using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create', 'export')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_kind",
                schema: "platform",
                table: "jobs",
                sql: "kind IN ('suspend', 'resume', 'create')");
        }
    }
}
