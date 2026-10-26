using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "files",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_path = table.Column<string>(type: "text", nullable: false),
                    owner_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    storage_kind = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_state", "state IN ('pending','attached','deleted')");
                    table.CheckConstraint("ck_files_storage_kind", "storage_kind = 'database'");
                });

            migrationBuilder.CreateTable(
                name: "file_contents",
                schema: "axis",
                columns: table => new
                {
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_contents", x => x.file_id);
                    table.ForeignKey(
                        name: "fk_file_contents_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "axis",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_contents",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "files",
                schema: "axis");
        }
    }
}
