using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordImportFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "format",
                schema: "axis",
                table: "record_imports",
                type: "text",
                nullable: false,
                defaultValue: "csv");

            migrationBuilder.AddCheckConstraint(
                name: "ck_record_imports_format",
                schema: "axis",
                table: "record_imports",
                sql: "format IN ('csv', 'xlsx')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_record_imports_format",
                schema: "axis",
                table: "record_imports");

            migrationBuilder.DropColumn(
                name: "format",
                schema: "axis",
                table: "record_imports");
        }
    }
}
