using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlans : Migration
    {
        private static readonly Guid FreePlanId = Guid.Parse("11111111-1111-1111-1111-111111111101");
        private static readonly Guid ProPlanId = Guid.Parse("11111111-1111-1111-1111-111111111102");
        private static readonly Guid EnterprisePlanId = Guid.Parse("11111111-1111-1111-1111-111111111103");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    monthly_price_cents = table.Column<int>(type: "integer", nullable: false),
                    max_workflows = table.Column<int>(type: "integer", nullable: true),
                    max_executions_per_month = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    max_storage_megabytes = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_available_for_new_signups = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_slug",
                table: "subscription_plans",
                column: "slug",
                unique: true);

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[]
                {
                    "id", "name", "slug", "monthly_price_cents", "max_workflows",
                    "max_executions_per_month", "max_users", "max_storage_megabytes",
                    "is_active", "is_available_for_new_signups",
                },
                values: new object[,]
                {
                    { FreePlanId, "Free", "free", 0, 3, 1000, 3, 500L, true, true },
                    { ProPlanId, "Pro", "pro", 4900, 25, 50000, 25, 10240L, true, true },
                    { EnterprisePlanId, "Enterprise", "enterprise", 0, null, null, null, null, true, false },
                });

            migrationBuilder.CreateTable(
                name: "organization_plan_change_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_plan_change_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_organization_plan_change_logs_organization_id",
                table: "organization_plan_change_logs",
                column: "organization_id");

            migrationBuilder.AddColumn<Guid>(
                name: "subscription_plan_id",
                table: "organizations",
                type: "uuid",
                nullable: false,
                defaultValue: FreePlanId);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subscription_plan_id",
                table: "organizations");

            migrationBuilder.DropTable(
                name: "organization_plan_change_logs");

            migrationBuilder.DropTable(
                name: "subscription_plans");
        }
    }
}
