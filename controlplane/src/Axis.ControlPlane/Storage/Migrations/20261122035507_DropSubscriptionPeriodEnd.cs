using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class DropSubscriptionPeriodEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D168 makes the paid-through date the one date of a paid tenant, see docs/reference/subscriptions.md#one-clock. A tenant that an old
            // payment made active keeps its paid flag and its next date: the paid-through date is the day before the end of its period.
            migrationBuilder.Sql("UPDATE control_plane.subscriptions SET paid_through = ((period_ends_at AT TIME ZONE 'UTC') - interval '1 day')::date WHERE paid_through IS NULL AND period_ends_at IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "period_ends_at",
                schema: "control_plane",
                table: "subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "period_ends_at",
                schema: "control_plane",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
