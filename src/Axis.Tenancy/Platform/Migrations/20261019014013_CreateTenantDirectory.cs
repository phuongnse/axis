using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class CreateTenantDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    database_host = table.Column<string>(type: "text", nullable: false),
                    database_port = table.Column<int>(type: "integer", nullable: false),
                    database_name = table.Column<string>(type: "text", nullable: false),
                    runtime_user = table.Column<string>(type: "text", nullable: false),
                    schema_user = table.Column<string>(type: "text", nullable: false),
                    runtime_password = table.Column<byte[]>(type: "bytea", nullable: false),
                    schema_password = table.Column<byte[]>(type: "bytea", nullable: false),
                    oidc_authority = table.Column<string>(type: "text", nullable: false),
                    oidc_client_id = table.Column<string>(type: "text", nullable: false),
                    oidc_client_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    claim_user_id = table.Column<string>(type: "text", nullable: true),
                    claim_display_name = table.Column<string>(type: "text", nullable: true),
                    claim_roles = table.Column<string>(type: "text", nullable: true),
                    endpoint_audience = table.Column<string>(type: "text", nullable: true),
                    schema_version = table.Column<string>(type: "text", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_status", "status IN ('provisioning', 'active', 'suspended')");
                });

            migrationBuilder.CreateTable(
                name: "tenant_bindings",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    secret_value = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_bindings", x => new { x.tenant_id, x.name });
                    table.CheckConstraint("ck_tenant_bindings_kind", "kind IN ('url', 'secret')");
                    table.ForeignKey(
                        name: "fk_tenant_bindings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_hosts",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    host = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_hosts", x => new { x.tenant_id, x.host });
                    table.ForeignKey(
                        name: "fk_tenant_hosts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // EF Core cannot model an expression index, so these two are plain SQL.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_tenant_hosts_host ON platform.tenant_hosts (lower(host));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_tenant_bindings_tenant_id_name ON platform.tenant_bindings (tenant_id, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX platform.ux_tenant_bindings_tenant_id_name;");
            migrationBuilder.Sql("DROP INDEX platform.ux_tenant_hosts_host;");

            migrationBuilder.DropTable(
                name: "tenant_bindings",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_hosts",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "platform");
        }
    }
}
