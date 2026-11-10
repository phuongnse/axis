using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class FixSubscriptionCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A subscription's currency is fixed once it has a ledger entry, see docs/reference/payments.md#invoices.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION control_plane.reject_subscription_currency_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM billing.ledger_entries WHERE subscription_id = OLD.id) THEN
                        RAISE EXCEPTION 'the currency of subscription % is fixed once it has a ledger entry', OLD.id USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER subscriptions_currency_fixed
                    BEFORE UPDATE OF currency ON control_plane.subscriptions
                    FOR EACH ROW WHEN (OLD.currency IS DISTINCT FROM NEW.currency)
                    EXECUTE FUNCTION control_plane.reject_subscription_currency_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER subscriptions_currency_fixed ON control_plane.subscriptions;");
            migrationBuilder.Sql("DROP FUNCTION control_plane.reject_subscription_currency_change();");
        }
    }
}
