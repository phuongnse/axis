using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed partial class QuotaDefaultsTests
{
    [Fact]
    public void Every_quota_key_of_the_code_has_a_built_in_default()
    {
        var keys = typeof(QuotaKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Contains(key, QuotaKeys.All);
            Assert.True(QuotaDefaults.BuiltIn.ContainsKey(key), $"'{key}' has no built-in default.");
        }

        Assert.Equal(keys.Count, QuotaKeys.All.Count);
        Assert.Equal(keys.Count, QuotaDefaults.BuiltIn.Count);
    }

    [Fact]
    public void The_quotas_table_of_the_docs_matches_the_keys_and_defaults_of_the_code()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "tenant-quotas.md"));
        var headers = Enumerable.Range(0, lines.Length).Where(index => QuotasTableHeader().IsMatch(lines[index])).ToList();
        Assert.True(headers.Count == 1, $"Expected exactly one table with header '| Quota | What it counts | Default | Kind |' in docs/reference/tenant-quotas.md, found {headers.Count}.");

        var documented = new Dictionary<string, long?>(StringComparer.Ordinal);
        var problems = new List<string>();
        // The line after the header is the separator row, not a quota.
        for (var i = headers[0] + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var cells = lines[i].Split('|');
            if (cells.Length < 5 || !QuotaKeyCell().IsMatch(cells[1].Trim()))
            {
                problems.Add($"A row of the quotas table in docs/reference/tenant-quotas.md has no quota key or no default: {lines[i].Trim()}");
                continue;
            }

            var key = QuotaKeyCell().Match(cells[1].Trim()).Groups[1].Value;
            if (!documented.TryAdd(key, ParseDefault(cells[3].Trim(), key)))
            {
                problems.Add($"'{key}' is in the quotas table of docs/reference/tenant-quotas.md more than once.");
            }
        }

        foreach (var key in QuotaKeys.All.Where(key => !documented.ContainsKey(key)))
        {
            problems.Add($"Missing from the quotas table in docs/reference/tenant-quotas.md: {key}.");
        }

        foreach (var key in documented.Keys.Where(key => !QuotaKeys.All.Contains(key)))
        {
            problems.Add($"Not a quota of QuotaKeys: {key}.");
        }

        foreach (var key in QuotaDefaults.BuiltIn.Keys.Where(key => !documented.ContainsKey(key) && !QuotaKeys.All.Contains(key)))
        {
            problems.Add($"Missing from the quotas table in docs/reference/tenant-quotas.md: {key}.");
        }

        foreach (var (key, value) in documented)
        {
            if (QuotaDefaults.BuiltIn.TryGetValue(key, out var builtIn) && value != builtIn)
            {
                problems.Add($"'{key}' defaults to {Show(value)} in the docs and {Show(builtIn)} in QuotaDefaults.BuiltIn.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(" ", problems));
    }

    [Fact]
    public void A_cell_with_no_settings_gets_the_built_in_defaults()
    {
        var defaults = QuotaDefaults.Load(Settings(), Cell());

        foreach (var (quota, expected) in QuotaDefaults.BuiltIn)
        {
            Assert.Equal(expected, defaults[quota]);
        }
    }

    [Fact]
    public void A_setting_overrides_the_default_of_one_quota_in_a_cell()
    {
        var defaults = QuotaDefaults.Load(Settings(("Quotas:Defaults:users", "5"), ("Quotas:Defaults:authoringRequests", "0")), Cell());

        Assert.Equal(5, defaults["users"]);
        Assert.Equal(0, defaults["authoringRequests"]);
        Assert.Equal(10, defaults["applications"]);
    }

    [Theory]
    [InlineData("users", "many")]
    [InlineData("users", "")]
    [InlineData("users", "1.5")]
    [InlineData("users", "-1")]
    [InlineData("applications", "+3")]
    [InlineData("seats", "5")]
    public void A_wrong_setting_in_a_cell_is_refused_naming_the_setting_and_not_the_value(string quota, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QuotaDefaults.Load(Settings(($"Quotas:Defaults:{quota}", value)), Cell()));

        Assert.Contains($"Quotas:Defaults:{quota}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_wrong_setting_is_named()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QuotaDefaults.Load(
            Settings(("Quotas:Defaults:users", "many"), ("Quotas:Defaults:seats", "5")),
            Cell()));

        Assert.Contains("Quotas:Defaults:users", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Quotas:Defaults:seats", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_cell_the_settings_are_ignored()
    {
        var defaults = QuotaDefaults.Load(Settings(("Quotas:Defaults:users", "many"), ("Quotas:Defaults:seats", "-1")), new CellOptions());

        Assert.Equal(50, defaults["users"]);
    }

    [Fact]
    public void An_unknown_quota_is_refused()
    {
        var defaults = QuotaDefaults.Load(Settings(), Cell());

        Assert.Throws<ArgumentException>(() => defaults["seats"]);
    }

    [GeneratedRegex(@"^\|\s*Quota\s*\|\s*What it counts\s*\|\s*Default\s*\|")]
    private static partial Regex QuotasTableHeader();

    [GeneratedRegex(@"^`([A-Za-z]+)`$")]
    private static partial Regex QuotaKeyCell();

    [GeneratedRegex(@"^([0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\s*(KiB|MiB|GiB)?$")]
    private static partial Regex DefaultCell();

    // The default of a row of the quotas table: a number with thousands commas, a number with KiB, MiB or GiB, or None.
    private static long? ParseDefault(string cell, string quota)
    {
        if (cell == "None")
        {
            return null;
        }

        var match = DefaultCell().Match(cell);
        if (!match.Success)
        {
            Assert.Fail($"The default '{cell}' of '{quota}' in docs/reference/tenant-quotas.md is not a number, a number with KiB, MiB or GiB, or None.");
        }

        var number = long.Parse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Value switch
        {
            "KiB" => 1024L,
            "MiB" => 1024L * 1024,
            "GiB" => 1024L * 1024 * 1024,
            _ => 1L,
        };
        return number * unit;
    }

    private static string Show(long? value) => value is null ? "None" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    private static CellOptions Cell() => new() { Id = "vn-1", Region = "vn", TenantDomain = "vn.example.com" };

    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(row => new KeyValuePair<string, string?>(row.Key, row.Value))).Build();
}
