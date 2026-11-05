using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.RegionDirectory.Migrations
{
    /// <inheritdoc />
    public partial class CreateDirectorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.CreateTable(
                name: "held",
                schema: "directory",
                columns: table => new
                {
                    kind = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    held_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_held", x => new { x.kind, x.value });
                    table.CheckConstraint("ck_held_kind", "kind IN ('tenant', 'host')");
                });

            migrationBuilder.CreateTable(
                name: "regions",
                schema: "directory",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    tenant_domain = table.Column<string>(type: "text", nullable: false),
                    api_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "cells",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    region_code = table.Column<string>(type: "text", nullable: false),
                    internal_url = table.Column<string>(type: "text", nullable: false),
                    cap = table.Column<int>(type: "integer", nullable: false),
                    tenant_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    takes_new_tenants = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cells", x => x.id);
                    table.CheckConstraint("ck_cells_cap", "cap > 0");
                    table.CheckConstraint("ck_cells_tenant_count", "tenant_count >= 0");
                    table.ForeignKey(
                        name: "fk_cells_region_code",
                        column: x => x.region_code,
                        principalSchema: "directory",
                        principalTable: "regions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    region_code = table.Column<string>(type: "text", nullable: false),
                    cell_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenants_cell_id",
                        column: x => x.cell_id,
                        principalSchema: "directory",
                        principalTable: "cells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenants_region_code",
                        column: x => x.region_code,
                        principalSchema: "directory",
                        principalTable: "regions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hosts",
                schema: "directory",
                columns: table => new
                {
                    host = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    cell_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosts", x => x.host);
                    table.ForeignKey(
                        name: "fk_hosts_cell_id",
                        column: x => x.cell_id,
                        principalSchema: "directory",
                        principalTable: "cells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hosts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "directory",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cells_region_code",
                schema: "directory",
                table: "cells",
                column: "region_code");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_cell_id",
                schema: "directory",
                table: "hosts",
                column: "cell_id");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_tenant_id",
                schema: "directory",
                table: "hosts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_cell_id",
                schema: "directory",
                table: "tenants",
                column: "cell_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_region_code",
                schema: "directory",
                table: "tenants",
                column: "region_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "held",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "hosts",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "cells",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "regions",
                schema: "directory");
        }
    }
}
