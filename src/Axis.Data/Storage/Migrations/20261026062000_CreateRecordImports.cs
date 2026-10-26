using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateRecordImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "record_imports",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_id = table.Column<string>(type: "text", nullable: false),
                    uploader_name = table.Column<string>(type: "text", nullable: false),
                    uploader_roles = table.Column<string[]>(type: "text[]", nullable: false),
                    key = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    file = table.Column<byte[]>(type: "bytea", nullable: true),
                    errors = table.Column<string>(type: "jsonb", nullable: true),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_imports", x => x.id);
                    table.CheckConstraint("ck_record_imports_state", "state IN ('pending', 'checked', 'failed', 'committed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_record_imports_work_item_id",
                schema: "axis",
                table: "record_imports",
                column: "work_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "record_imports",
                schema: "axis");
        }
    }
}
