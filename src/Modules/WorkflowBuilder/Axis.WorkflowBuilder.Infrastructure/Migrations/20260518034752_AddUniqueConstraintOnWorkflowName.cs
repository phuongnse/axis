using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.WorkflowBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueConstraintOnWorkflowName : Migration
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
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_organization_id_name",
                table: "workflow_definitions",
                columns: new[] { "organization_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_definitions");
        }
    }
}
