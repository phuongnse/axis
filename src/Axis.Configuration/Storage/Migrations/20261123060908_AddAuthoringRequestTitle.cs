using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoringRequestTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A plan stored as text becomes the plan's shape with that text as its summary and every group empty.
            migrationBuilder.Sql(
                """
                ALTER TABLE axis.authoring_requests ALTER COLUMN plan TYPE jsonb
                USING CASE WHEN plan IS NULL THEN NULL ELSE jsonb_build_object(
                    'summary', plan, 'questions', '[]'::jsonb, 'changes', '[]'::jsonb, 'decisions', '[]'::jsonb,
                    'risks', '[]'::jsonb, 'checks', '[]'::jsonb, 'checklist', '[]'::jsonb, 'details', '[]'::jsonb) END
                """);

            migrationBuilder.AddColumn<string>(
                name: "title",
                schema: "axis",
                table: "authoring_requests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "title",
                schema: "axis",
                table: "authoring_requests");

            migrationBuilder.Sql(
                "ALTER TABLE axis.authoring_requests ALTER COLUMN plan TYPE text USING plan->>'summary'");
        }
    }
}
