using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidSubscriptionStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_state",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_state",
                schema: "control_plane",
                table: "subscriptions",
                sql: "state IN ('trialing', 'active', 'pastDue', 'readOnly', 'cancelled', 'deleting', 'deleted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_state",
                schema: "control_plane",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_state",
                schema: "control_plane",
                table: "subscriptions",
                sql: "state IN ('trialing', 'readOnly', 'deleting', 'deleted')");
        }
    }
}
