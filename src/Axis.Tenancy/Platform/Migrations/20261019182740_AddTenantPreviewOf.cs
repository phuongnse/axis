using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPreviewOf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preview_of",
                schema: "platform",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_preview_of",
                schema: "platform",
                table: "tenants",
                column: "preview_of",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_tenants_preview_of",
                schema: "platform",
                table: "tenants",
                column: "preview_of",
                principalSchema: "platform",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenants_tenants_preview_of",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tenants_preview_of",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "preview_of",
                schema: "platform",
                table: "tenants");
        }
    }
}
