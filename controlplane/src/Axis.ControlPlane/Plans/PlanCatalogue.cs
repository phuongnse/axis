using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Tenancy;

namespace Axis.ControlPlane.Plans;

/// <summary>The currencies of a price, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
public static class PlanCurrencies
{
    public const string Vnd = "VND";

    public const string Usd = "USD";

    internal static readonly string[] All = [Vnd, Usd];
}

/// <summary>The periods of a subscription, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
public static class PlanPeriods
{
    public const string Month = "month";

    public const string Year = "year";

    /// <summary>A year costs this many months, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
    internal const long MonthsPerYear = 10;
}

/// <summary>A user pack of a plan version, with its monthly prices.</summary>
public sealed record PlanAddOn(string Code, long Users, IReadOnlyDictionary<string, long> MonthlyPrices)
{
    /// <summary>The price of the pack for a period in a currency, in the currency's smallest unit.</summary>
    /// <exception cref="ArgumentException">The currency or the period is unknown.</exception>
    public long Price(string currency, string period) => PlanPrices.Of(MonthlyPrices, currency, period);
}

/// <summary>One version of a plan, see <c>docs/reference/plans.md#the-catalogue</c>.</summary>
public sealed record PlanVersion(
    string Code,
    int Version,
    string NameEn,
    string NameVi,
    bool Trial,
    long IncludedUsers,
    IReadOnlyDictionary<string, long> Quotas,
    IReadOnlyDictionary<string, long> MonthlyPrices,
    IReadOnlyList<PlanAddOn> AddOns)
{
    /// <summary>The price of the plan for a period in a currency, in the currency's smallest unit.</summary>
    /// <exception cref="ArgumentException">The currency or the period is unknown.</exception>
    public long Price(string currency, string period) => PlanPrices.Of(MonthlyPrices, currency, period);
}

internal static class PlanPrices
{
    // Computed, never stored: a year costs 10 months, see docs/reference/plans.md#the-catalogue.
    public static long Of(IReadOnlyDictionary<string, long> monthly, string currency, string period)
    {
        if (!monthly.TryGetValue(currency, out var month))
        {
            throw new ArgumentException("The currency has no price.", nameof(currency));
        }

        return period switch
        {
            PlanPeriods.Month => month,
            PlanPeriods.Year => checked(month * PlanPeriods.MonthsPerYear),
            _ => throw new ArgumentException("The period is not month or year.", nameof(period)),
        };
    }
}

/// <summary>
/// The plan versions of <c>ControlPlane:Plans</c>, checked at start, see <c>docs/reference/plans.md#the-catalogue</c>. A subscription pins
/// the code and version it bought, so a later version never reaches it, and a pinned version is never removed from the settings.
/// </summary>
public sealed partial class PlanCatalogue
{
    private const string Section = $"{ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.Plans)}";

    // A yearly price is 10 times the monthly one, so the monthly one must leave room for it.
    private const long MaxMonthlyPrice = long.MaxValue / PlanPeriods.MonthsPerYear;

    private readonly Dictionary<(string Code, int Version), PlanVersion> _byKey;

    private PlanCatalogue(IReadOnlyList<PlanVersion> versions)
    {
        Versions = versions;
        _byKey = versions.ToDictionary(v => (v.Code, v.Version));
    }

    /// <summary>Every plan version, in the order of the settings.</summary>
    public IReadOnlyList<PlanVersion> Versions { get; }

    /// <summary>The plan version with this code and version, or null.</summary>
    public PlanVersion? Find(string code, int version) => _byKey.GetValueOrDefault((code, version));

    /// <summary>Checks the plans and builds the catalogue.</summary>
    /// <exception cref="InvalidOperationException">A plan is wrong. The message names every wrong setting and never a value.</exception>
    internal static PlanCatalogue Create(IReadOnlyList<PlanOptions> plans)
    {
        var problems = Problems(plans);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The control plane configuration is invalid. {string.Join(" ", problems)}");
        }

        var required = RequiredQuotas();
        return new PlanCatalogue(plans.Select(plan => new PlanVersion(
            plan.Code!,
            int.Parse(plan.Version!, NumberStyles.None, CultureInfo.InvariantCulture),
            plan.Name.En!,
            plan.Name.Vi!,
            plan.Trial,
            Parse(plan.IncludedUsers!),
            required.ToDictionary(key => key, key => Parse(plan.Quotas[key]), StringComparer.Ordinal),
            Prices(plan.Prices),
            plan.AddOns.Select(addOn => new PlanAddOn(addOn.Code!, Parse(addOn.Users!), Prices(addOn.Prices))).ToList())).ToList());
    }

    /// <summary>Stops the start when a subscription pins a plan version that the settings no longer list.</summary>
    /// <exception cref="InvalidOperationException">The message names each missing plan code and version.</exception>
    internal void CheckPinned(IEnumerable<(string Code, int Version)> pinned)
    {
        var missing = pinned
            .Distinct()
            .Where(pin => !_byKey.ContainsKey(pin))
            .Select(pin => $"The setting {Section} has no plan {pin.Code} version {pin.Version.ToString(CultureInfo.InvariantCulture)}, which a subscription pins.")
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"The control plane configuration is invalid. {string.Join(" ", missing)}");
        }
    }

    /// <summary>Every problem of the plans. A message names the setting and, once the code and version are valid, the plan, and never holds a value.</summary>
    internal static List<string> Problems(IReadOnlyList<PlanOptions> plans)
    {
        var problems = new List<string>();
        var seen = new HashSet<(string, int)>();
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var prefix = $"{Section}:{index.ToString(CultureInfo.InvariantCulture)}";
            var codeIsValid = false;
            if (string.IsNullOrWhiteSpace(plan.Code))
            {
                problems.Add($"The setting {prefix}:{nameof(PlanOptions.Code)} is required.");
            }
            else if (!Code().IsMatch(plan.Code))
            {
                problems.Add($"The setting {prefix}:{nameof(PlanOptions.Code)} must be lowercase letters and digits, joined by single hyphens.");
            }
            else
            {
                codeIsValid = true;
            }

            var versionIsValid = false;
            if (WholeNumberProblem(plan.Version, 1, int.MaxValue) is { } versionProblem)
            {
                problems.Add($"The setting {prefix}:{nameof(PlanOptions.Version)} {versionProblem}");
            }
            else
            {
                versionIsValid = true;
            }

            var of = "";
            if (codeIsValid && versionIsValid)
            {
                var version = int.Parse(plan.Version!, NumberStyles.None, CultureInfo.InvariantCulture);
                of = $" of the plan {plan.Code} version {version.ToString(CultureInfo.InvariantCulture)}";
                if (!seen.Add((plan.Code!, version)))
                {
                    problems.Add($"The setting {prefix}:{nameof(PlanOptions.Version)}{of} repeats an earlier plan.");
                }
            }

            AddTextProblem(problems, $"{prefix}:{nameof(PlanOptions.Name)}:en", of, plan.Name.En);
            AddTextProblem(problems, $"{prefix}:{nameof(PlanOptions.Name)}:vi", of, plan.Name.Vi);
            AddWholeNumberProblem(problems, $"{prefix}:{nameof(PlanOptions.IncludedUsers)}", of, plan.IncludedUsers, 1, long.MaxValue);
            QuotaProblems(problems, prefix, of, plan);

            if (plan.Trial)
            {
                if (plan.Prices.Count > 0)
                {
                    problems.Add($"The setting {prefix}:{nameof(PlanOptions.Prices)}{of} must be empty, because the plan is a trial plan.");
                }

                if (plan.AddOns.Count > 0)
                {
                    problems.Add($"The setting {prefix}:{nameof(PlanOptions.AddOns)}{of} must be empty, because the plan is a trial plan.");
                }

                // A trial plan runs no package, see docs/reference/plans.md#the-catalogue. An invalid value has its own problem.
                if (plan.Quotas.TryGetValue(QuotaKeys.PackageCpuSecondsPerDay, out var packageCpu)
                    && long.TryParse(packageCpu, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    && seconds != 0)
                {
                    problems.Add($"The setting {prefix}:{nameof(PlanOptions.Quotas)}:{QuotaKeys.PackageCpuSecondsPerDay}{of} must be 0, because the plan is a trial plan.");
                }

                continue;
            }

            PriceProblems(problems, $"{prefix}:{nameof(PlanOptions.Prices)}", of, plan.Prices);
            AddOnProblems(problems, prefix, of, plan.AddOns);
        }

        return problems;
    }

    // Every quota key of the platform except the users, which the plan's included users and add-ons set.
    private static List<string> RequiredQuotas() => QuotaKeys.All.Where(key => key != QuotaKeys.Users).ToList();

    private static void QuotaProblems(List<string> problems, string prefix, string of, PlanOptions plan)
    {
        var required = RequiredQuotas();
        foreach (var key in required)
        {
            var setting = $"{prefix}:{nameof(PlanOptions.Quotas)}:{key}";
            if (!plan.Quotas.TryGetValue(key, out var value))
            {
                problems.Add($"The setting {setting}{of} is required.");
            }
            else
            {
                AddWholeNumberProblem(problems, setting, of, value, 0, long.MaxValue);
            }
        }

        foreach (var key in plan.Quotas.Keys.Where(key => !required.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"The setting {prefix}:{nameof(PlanOptions.Quotas)}:{key}{of} is not a quota that a plan sets.");
        }
    }

    private static void AddOnProblems(List<string> problems, string prefix, string of, List<PlanAddOnOptions> addOns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < addOns.Count; index++)
        {
            var addOn = addOns[index];
            var path = $"{prefix}:{nameof(PlanOptions.AddOns)}:{index.ToString(CultureInfo.InvariantCulture)}";
            if (string.IsNullOrWhiteSpace(addOn.Code))
            {
                problems.Add($"The setting {path}:{nameof(PlanAddOnOptions.Code)}{of} is required.");
            }
            else if (!Code().IsMatch(addOn.Code))
            {
                problems.Add($"The setting {path}:{nameof(PlanAddOnOptions.Code)}{of} must be lowercase letters and digits, joined by single hyphens.");
            }
            else if (!seen.Add(addOn.Code))
            {
                problems.Add($"The setting {path}:{nameof(PlanAddOnOptions.Code)}{of} repeats the code of an earlier add-on.");
            }

            AddWholeNumberProblem(problems, $"{path}:{nameof(PlanAddOnOptions.Users)}", of, addOn.Users, 1, long.MaxValue);
            PriceProblems(problems, $"{path}:{nameof(PlanAddOnOptions.Prices)}", of, addOn.Prices);
        }
    }

    // A paid plan and an add-on carry exactly one price in each currency.
    private static void PriceProblems(List<string> problems, string path, string of, List<PlanPriceOptions> prices)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < prices.Count; index++)
        {
            var price = prices[index];
            var pricePath = $"{path}:{index.ToString(CultureInfo.InvariantCulture)}";
            if (string.IsNullOrWhiteSpace(price.Currency))
            {
                problems.Add($"The setting {pricePath}:{nameof(PlanPriceOptions.Currency)}{of} is required.");
            }
            else if (!PlanCurrencies.All.Contains(price.Currency, StringComparer.Ordinal))
            {
                problems.Add($"The setting {pricePath}:{nameof(PlanPriceOptions.Currency)}{of} must be {PlanCurrencies.Vnd} or {PlanCurrencies.Usd}.");
            }
            else if (!seen.Add(price.Currency))
            {
                problems.Add($"The setting {pricePath}:{nameof(PlanPriceOptions.Currency)}{of} repeats the currency of an earlier price.");
            }

            AddWholeNumberProblem(problems, $"{pricePath}:{nameof(PlanPriceOptions.Month)}", of, price.Month, 1, MaxMonthlyPrice);
        }

        foreach (var currency in PlanCurrencies.All.Where(currency => !seen.Contains(currency)))
        {
            problems.Add($"The setting {path}{of} has no {currency} price.");
        }
    }

    private static void AddTextProblem(List<string> problems, string setting, string of, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"The setting {setting}{of} is required.");
        }
    }

    private static void AddWholeNumberProblem(List<string> problems, string setting, string of, string? value, long minimum, long maximum)
    {
        if (WholeNumberProblem(value, minimum, maximum) is { } problem)
        {
            problems.Add($"The setting {setting}{of} {problem}");
        }
    }

    // The sign, the decimal point and the exponent are not digits, so a value such as 990000.5 is a problem here and never reaches the binder's own message.
    private static string? WholeNumberProblem(string? value, long minimum, long maximum)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "is required.";
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? null
            : $"must be a whole number of digits only, from {minimum.ToString(CultureInfo.InvariantCulture)} to {maximum.ToString(CultureInfo.InvariantCulture)}.";
    }

    private static long Parse(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static Dictionary<string, long> Prices(List<PlanPriceOptions> prices) =>
        prices.ToDictionary(price => price.Currency!, price => Parse(price.Month!), StringComparer.Ordinal);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Code();
}
