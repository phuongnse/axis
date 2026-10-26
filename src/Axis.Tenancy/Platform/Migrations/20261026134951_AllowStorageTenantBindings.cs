using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Tenancy.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AllowStorageTenantBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_bindings_kind",
                schema: "platform",
                table: "tenant_bindings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_bindings_kind",
                schema: "platform",
                table: "tenant_bindings",
                sql: "kind IN ('url', 'secret', 'storage')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_bindings_kind",
                schema: "platform",
                table: "tenant_bindings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_bindings_kind",
                schema: "platform",
                table: "tenant_bindings",
                sql: "kind IN ('url', 'secret')");
        }
    }
}
