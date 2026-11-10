using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "control_plane",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period",
                schema: "control_plane",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_code",
                schema: "control_plane",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "plan_version",
                schema: "control_plane",
                table: "subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_currency",
                schema: "control_plane",
                table: "subscriptions",
                sql: "currency IN ('VND', 'USD')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_period",
                schema: "control_plane",
                table: "subscriptions",
                sql: "period IN ('month', 'year')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_plan",
                schema: "control_plane",
                table: "subscriptions",
                sql: "(plan_code IS NULL) = (plan_version IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions",
                sql: "(currency IS NULL) = (period IS NULL) AND (currency IS NULL OR plan_code IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_currency",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_period",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_plan",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_purchase",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "period",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "plan_code",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "plan_version",
                schema: "control_plane",
                table: "subscriptions");
        }
    }
}
