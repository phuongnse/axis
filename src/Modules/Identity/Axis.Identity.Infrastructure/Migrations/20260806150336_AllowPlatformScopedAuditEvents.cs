using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowPlatformScopedAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "workspace_id",
                table: "identity_audit_outbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_identity_audit_outbox_scope",
                table: "identity_audit_outbox",
                sql: "actor_kind IN ('System', 'Anonymous') OR workspace_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM identity_audit_outbox WHERE workspace_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required audit workspace scope while platform-scoped outbox records exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_identity_audit_outbox_scope",
                table: "identity_audit_outbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "workspace_id",
                table: "identity_audit_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
