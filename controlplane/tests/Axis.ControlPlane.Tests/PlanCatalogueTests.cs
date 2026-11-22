using System.Text;
using Axis.ControlPlane.Plans;
using Microsoft.Extensions.Configuration;

namespace Axis.ControlPlane.Tests;

/// <summary>The check of <c>ControlPlane:Plans</c> at start, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
public sealed class PlanCatalogueTests
{
    // The nine quota keys are listed on purpose: a new quota key makes the valid catalogue fail until this fixture carries it too.
    private const string Quotas = """
        "quotas": {
          "storage": 21474836480,
          "applications": 10,
          "emailsPerDay": 2000,
          "packageCpuSecondsPerDay": 3600,
          "concurrentImports": 2,
          "concurrentConnectorCalls": 10,
          "concurrentQueries": 10,
          "workerWeight": 2,
          "authoringRequests": 200
        }
        """;

    // A trial plan runs no package, so the same quotas with a package run time of 0.
    private static readonly string _trialQuotas = Quotas.Replace("\"packageCpuSecondsPerDay\": 3600", "\"packageCpuSecondsPerDay\": 0", StringComparison.Ordinal);

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
    public void A_trial_plan_with_an_add_on_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, trial: true)}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:AddOns", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trial_plan_with_a_package_run_time_is_refused_naming_the_plan_and_the_setting_and_not_the_value()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, quotas: Quotas, trial: true, addOns: "\"addOns\": []")}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Quotas:packageCpuSecondsPerDay", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("3600", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trial_plan_with_no_add_on_is_valid()
    {
        var plans = Bind($"[{Plan(1, trial: true, addOns: "\"addOns\": []")}]");

        Assert.Empty(PlanCatalogue.Problems(plans));
    }

    [Fact]
    public void A_trial_plan_with_more_than_ten_included_users_is_refused_naming_the_plan_and_the_setting_and_not_the_value()
    {
        var plans = Bind($"[{Plan(1)}, {Plan(2, trial: true, addOns: "\"addOns\": []", includedUsers: 12345678901)}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:IncludedUsers", problem, StringComparison.Ordinal);
        Assert.Contains("team version 2", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678901", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_trial_plan_code_is_refused_naming_the_plan_and_the_setting()
    {
        var plans = Bind($"[{TrialPlan(1, "starter")}, {TrialPlan(1, "pilot")}]");

        var problem = Assert.Single(PlanCatalogue.Problems(plans));

        Assert.Contains("ControlPlane:Plans:1:Trial", problem, StringComparison.Ordinal);
        Assert.Contains("pilot version 1", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trial_plan_is_the_newest_version_of_the_trial_code()
    {
        var catalogue = PlanCatalogue.Create(Bind($"[{TrialPlan(3, "starter")}, {Plan(9)}, {TrialPlan(1, "starter")}]"));

        Assert.Equal(3, catalogue.Trial!.Version);
    }

    [Fact]
    public void A_catalogue_with_no_trial_plan_has_no_trial()
    {
        Assert.Null(PlanCatalogue.Create(Bind($"[{Plan(1)}]")).Trial);
        Assert.Null(PlanCatalogue.Create(Bind("[]")).Trial);
    }

    [Fact]
    public void A_plan_version_holds_its_names_included_users_quotas_and_add_ons_and_no_price()
    {
        var catalogue = PlanCatalogue.Create(Bind($"[{Plan(2)}]"));

        var plan = catalogue.Find("team", 2)!;

        Assert.Equal(("team", 2, "Team", "Nhóm", false, 25L), (plan.Code, plan.Version, plan.NameEn, plan.NameVi, plan.Trial, plan.IncludedUsers));
        Assert.Equal(21474836480L, plan.Quotas[Axis.Tenancy.QuotaKeys.Storage]);
        Assert.Equal(new PlanAddOn("users-10", 10), Assert.Single(plan.AddOns));
    }

    [Fact]
    public void A_catalogue_with_a_problem_stops_the_start_and_the_message_names_it()
    {
        var plans = Bind($"[{Plan(2, quotas: Quotas.Replace("\"workerWeight\": 2,", "", StringComparison.Ordinal))}]");

        var exception = Assert.Throws<InvalidOperationException>(() => PlanCatalogue.Create(plans));

        Assert.Contains("ControlPlane:Plans:0:Quotas:workerWeight", exception.Message, StringComparison.Ordinal);
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

    private static string Plan(
        int version,
        string? quotas = null,
        bool trial = false,
        string? addOns = null,
        string code = "team",
        long? includedUsers = null) => $$"""
        {
          "code": "{{code}}",
          "version": {{version}},
          "name": { "en": "Team", "vi": "Nhóm" },
          "includedUsers": {{includedUsers ?? (trial ? 10 : 25)}},
          "trial": {{(trial ? "true" : "false")}},
          {{quotas ?? (trial ? _trialQuotas : Quotas)}},
          {{addOns ?? """
          "addOns": [
            { "code": "users-10", "users": 10 }
          ]
          """}}
        }
        """;

    private static string TrialPlan(int version, string code) =>
        Plan(version, trial: true, addOns: "\"addOns\": []", code: code);

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
