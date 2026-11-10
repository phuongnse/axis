using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    sign_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.CheckConstraint("ck_subscriptions_state", "state IN ('trialing', 'readOnly', 'deleting', 'deleted')");
                    table.ForeignKey(
                        name: "fk_subscriptions_sign_up_id",
                        column: x => x.sign_up_id,
                        principalSchema: "control_plane",
                        principalTable: "sign_ups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_next_at",
                schema: "control_plane",
                table: "subscriptions",
                column: "next_at",
                filter: "state <> 'deleted'");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_sign_up_id",
                schema: "control_plane",
                table: "subscriptions",
                column: "sign_up_id");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_tenant_id",
                schema: "control_plane",
                table: "subscriptions",
                column: "tenant_id",
                unique: true,
                filter: "state <> 'deleted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "control_plane");
        }
    }
}
