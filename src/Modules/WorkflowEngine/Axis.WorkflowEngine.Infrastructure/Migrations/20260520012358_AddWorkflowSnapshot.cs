using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.WorkflowEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_snapshots",
                columns: table => new
                {
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    transitions = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_snapshots", x => x.workflow_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_snapshots");
        }
    }
}
