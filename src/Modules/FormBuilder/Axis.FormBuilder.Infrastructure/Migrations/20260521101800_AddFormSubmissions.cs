using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.FormBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_token = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submissions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_access_token",
                table: "form_submissions",
                column: "access_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_assignee_user_id_status",
                table: "form_submissions",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_execution_id_execution_step_id",
                table: "form_submissions",
                columns: new[] { "execution_id", "execution_step_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_submissions");
        }
    }
}
