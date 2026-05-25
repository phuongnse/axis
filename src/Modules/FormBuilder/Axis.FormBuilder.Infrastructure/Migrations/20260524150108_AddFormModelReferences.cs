using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.FormBuilder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormModelReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_model_references",
                columns: table => new
                {
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_broken = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_model_references", x => new { x.form_id, x.form_field_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_form_model_references_form_id",
                table: "form_model_references",
                column: "form_id");

            migrationBuilder.CreateIndex(
                name: "IX_form_model_references_model_id_organization_id",
                table: "form_model_references",
                columns: new[] { "model_id", "organization_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_model_references");
        }
    }
}
