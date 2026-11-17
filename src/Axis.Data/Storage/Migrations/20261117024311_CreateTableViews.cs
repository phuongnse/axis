using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "table_columns",
                schema: "axis",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    table_key = table.Column<string>(type: "text", nullable: false),
                    columns = table.Column<string[]>(type: "text[]", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_columns", x => new { x.user_id, x.table_key });
                });

            migrationBuilder.CreateTable(
                name: "table_views",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    table_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    search = table.Column<string>(type: "text", nullable: true),
                    filter = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_views", x => x.id);
                });

            // A person's views of a table have names that differ ignoring letter case. EF Core cannot model an index on lower(name).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_table_views_user_id_table_key_name ON axis.table_views (user_id, table_key, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX axis.ux_table_views_user_id_table_key_name;");

            migrationBuilder.DropTable(
                name: "table_columns",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "table_views",
                schema: "axis");
        }
    }
}
