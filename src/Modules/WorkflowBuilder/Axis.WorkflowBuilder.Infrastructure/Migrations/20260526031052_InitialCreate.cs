using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.WorkflowBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    team_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    transitions = table.Column<string>(type: "jsonb", nullable: false),
                    triggers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_form_references",
                columns: table => new
                {
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_broken = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_form_references", x => new { x.workflow_id, x.step_id });
                });

            migrationBuilder.CreateTable(
                name: "workflow_model_references",
                columns: table => new
                {
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_broken = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_model_references", x => new { x.workflow_id, x.model_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_team_account_id_name",
                table: "workflow_definitions",
                columns: new[] { "team_account_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_form_references_form_id",
                table: "workflow_form_references",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_form_references_team_account_id_form_id",
                table: "workflow_form_references",
                columns: new[] { "team_account_id", "form_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_model_references_model_id",
                table: "workflow_model_references",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_model_references_team_account_id_model_id",
                table: "workflow_model_references",
                columns: new[] { "team_account_id", "model_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "workflow_form_references");

            migrationBuilder.DropTable(
                name: "workflow_model_references");
        }
    }
}
