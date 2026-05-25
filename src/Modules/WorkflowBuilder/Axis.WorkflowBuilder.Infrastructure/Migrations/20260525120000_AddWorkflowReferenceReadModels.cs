using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.WorkflowBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowReferenceReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_form_references",
                columns: table => new
                {
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_broken = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_model_references", x => new { x.workflow_id, x.model_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_form_references_form_id",
                table: "workflow_form_references",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_form_references_organization_id_form_id",
                table: "workflow_form_references",
                columns: new[] { "organization_id", "form_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_model_references_model_id",
                table: "workflow_model_references",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_model_references_organization_id_model_id",
                table: "workflow_model_references",
                columns: new[] { "organization_id", "model_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_form_references");

            migrationBuilder.DropTable(
                name: "workflow_model_references");
        }
    }
}
