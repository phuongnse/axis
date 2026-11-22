using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class DropSubscriptionPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D168 keeps no billing period on a subscription, see docs/reference/plans.md#the-catalogue. The billing system holds it.
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_period",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "period",
                schema: "control_plane",
                table: "subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "period",
                schema: "control_plane",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_period",
                schema: "control_plane",
                table: "subscriptions",
                sql: "period IN ('month', 'year')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions",
                sql: "period IS NULL OR plan_code IS NOT NULL");
        }
    }
}
