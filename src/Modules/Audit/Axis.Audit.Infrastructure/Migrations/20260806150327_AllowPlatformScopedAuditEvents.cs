using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowPlatformScopedAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "workspace_id",
                table: "audit_records",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_audit_records_scope",
                table: "audit_records",
                sql: "actor_kind IN ('System', 'Anonymous') OR workspace_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM audit_records WHERE workspace_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required audit workspace scope while platform-scoped records exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_audit_records_scope",
                table: "audit_records");

            migrationBuilder.AlterColumn<Guid>(
                name: "workspace_id",
                table: "audit_records",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
