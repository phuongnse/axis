using System.Text;
using Axis.ControlPlane.Plans;
using Microsoft.Extensions.Configuration;

namespace Axis.ControlPlane.Tests;

/// <summary>The check of <c>ControlPlane:Plans</c> at start, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
public sealed class PlanCatalogueTests
{
    // The eight quota keys are listed on purpose: a new quota key makes the valid catalogue fail until this fixture carries it too.
    private const string Quotas = """
        "quotas": {
          "storage": 21474836480,
          "applications": 10,
          "emailsPerDay": 2000,
          "concurrentImports": 2,
          "concurrentConnectorCalls": 10,
          "concurrentQueries": 10,
          "workerWeight": 2,
          "authoringRequests": 200
        }
        """;

    private const string Prices = """
        "prices": [{ "currency": "VND", "month": 990000 }, { "currency": "USD", "month": 3900 }]
        """;

    private const string DecimalPrice = """
        "prices": [{ "currency": "VND", "month": 990000.5 }, { "currency": "USD", "month": 3900 }]
        """;

    private const string VndOnly = """
        "prices": [{ "currency": "VND", "month": 990000 }]
        """;

    [Fact]
    public void A_valid_catalogue_has_no_problems()
    {
        var plans = Bind($"[{Plan(2)}, {Plan(1)}]");

        Assert.Empty(PlanCatalogue.Problems(plans));
    }

    [Fact]
    public void An_empty_catalogue_is_valid()
    {
        Assert.Empty(PlanCatalogue.Problems(Bind("[]")));
    }

    [Fact]
    public void A_decimal_price_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, prices: DecimalPrice)}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Prices:0:Month", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("990000", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_currency_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, prices: VndOnly)}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Prices", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
        Assert.Contains("USD", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_quota_key_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, quotas: Quotas.Replace("\"workerWeight\": 2,", "", StringComparison.Ordinal))}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Quotas:workerWeight", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quota_keys_a_plan_needs_are_every_quota_key_of_the_platform_except_users()
    {
        // The quota list of the platform decides, so a new quota key fails here until the plans carry it.
        var plans = Bind($"[{Plan(1)}]");

        Assert.Equal(
            Axis.Tenancy.QuotaKeys.All.Where(key => key != Axis.Tenancy.QuotaKeys.Users).Order(StringComparer.Ordinal),
            plans[0].Quotas.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_users_quota_is_refused_because_a_plan_does_not_set_it()
    {
        var plans = Bind($"[{Plan(2, quotas: Quotas.Replace("\"storage\"", "\"users\": 5, \"storage\"", StringComparison.Ordinal))}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:0:Quotas:users", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_code_and_version_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(2)}, {Plan(2)}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Version", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trial_plan_with_a_price_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, trial: true, addOns: "\"addOns\": []")}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Prices", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trial_plan_with_no_price_is_valid()
    {
        var plans = Bind($"[{Plan(1, prices: "\"prices\": []", trial: true, addOns: "\"addOns\": []")}]");

        Assert.Empty(PlanCatalogue.Problems(plans));
    }

    [Fact]
    public void A_yearly_price_is_ten_times_the_monthly_price_for_a_plan_and_for_an_add_on()
    {
        var catalogue = PlanCatalogue.Create(Bind($"[{Plan(2)}]"));

        var plan = catalogue.Find("team", 2)!;

        Assert.Equal(990000, plan.Price(PlanCurrencies.Vnd, PlanPeriods.Month));
        Assert.Equal(9900000, plan.Price(PlanCurrencies.Vnd, PlanPeriods.Year));
        Assert.Equal(39000, plan.Price(PlanCurrencies.Usd, PlanPeriods.Year));
        var addOn = Assert.Single(plan.AddOns);
        Assert.Equal(290000, addOn.Price(PlanCurrencies.Vnd, PlanPeriods.Month));
        Assert.Equal(2900000, addOn.Price(PlanCurrencies.Vnd, PlanPeriods.Year));
        Assert.Equal(12000, addOn.Price(PlanCurrencies.Usd, PlanPeriods.Year));
        Assert.Throws<ArgumentException>(() => plan.Price("EUR", PlanPeriods.Month));
        Assert.Throws<ArgumentException>(() => plan.Price(PlanCurrencies.Vnd, "week"));
    }

    [Fact]
    public void A_catalogue_with_a_problem_stops_the_start_and_the_message_names_it()
    {
        var plans = Bind($"[{Plan(2, prices: VndOnly)}]");

        var exception = Assert.Throws<InvalidOperationException>(() => PlanCatalogue.Create(plans));

        Assert.Contains("ControlPlane:Plans:0:Prices", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_version_that_the_catalogue_lacks_stops_the_start_and_the_message_names_it()
    {
        var catalogue = PlanCatalogue.Create(Bind($"[{Plan(2)}]"));

        var exception = Assert.Throws<InvalidOperationException>(() => catalogue.CheckPinned([("team", 1), ("team", 1), ("team", 2)]));

        Assert.Contains("team version 1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("team version 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_version_that_the_catalogue_lists_passes_the_check()
    {
        var catalogue = PlanCatalogue.Create(Bind($"[{Plan(1)}, {Plan(2)}]"));

        catalogue.CheckPinned([("team", 1), ("team", 2)]);
        catalogue.CheckPinned([]);
    }

    private static string Plan(int version, string? quotas = null, string? prices = null, bool trial = false, string? addOns = null) => $$"""
        {
          "code": "team",
          "version": {{version}},
          "name": { "en": "Team", "vi": "Nhóm" },
          "includedUsers": 25,
          "trial": {{(trial ? "true" : "false")}},
          {{quotas ?? Quotas}},
          {{prices ?? Prices}},
          {{addOns ?? """
          "addOns": [
            { "code": "users-10", "users": 10, "prices": [{ "currency": "VND", "month": 290000 }, { "currency": "USD", "month": 1200 }] }
          ]
          """}}
        }
        """;

    // The shape of the docs example, bound as the host binds it.
    private static List<PlanOptions> Bind(string plansJson)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""{ "ControlPlane": { "plans": {{plansJson}} } }""")))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return options.Plans;
    }
}
