using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaOverridesAndAddOns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_overrides",
                schema: "control_plane",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quota = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quota_overrides", x => new { x.subscription_id, x.quota });
                    table.CheckConstraint("ck_quota_overrides_reason", "btrim(reason) <> ''");
                    table.CheckConstraint("ck_quota_overrides_value", "value >= 0");
                    table.ForeignKey(
                        name: "fk_quota_overrides_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "control_plane",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_add_ons",
                schema: "control_plane",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    add_on_code = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_add_ons", x => new { x.subscription_id, x.add_on_code });
                    table.CheckConstraint("ck_subscription_add_ons_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "fk_subscription_add_ons_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "control_plane",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_overrides",
                schema: "control_plane");

            migrationBuilder.DropTable(
                name: "subscription_add_ons",
                schema: "control_plane");
        }
    }
}
