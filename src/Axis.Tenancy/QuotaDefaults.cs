using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>
/// The default of each quota in a cell: the <c>Quotas:Defaults:&lt;quota&gt;</c> settings, each falling back to the value of the
/// quotas table in <c>docs/reference/tenant-quotas.md#quotas</c>. A <c>null</c> default means no limit.
/// </summary>
public sealed class QuotaDefaults
{
    public const string SectionName = "Quotas:Defaults";

    /// <summary>The defaults of the quotas table. <c>authoringRequests</c> has no default, which is no limit.</summary>
    public static IReadOnlyDictionary<string, long?> BuiltIn { get; } = new Dictionary<string, long?>(StringComparer.Ordinal)
    {
        [QuotaKeys.Storage] = 10L * 1024 * 1024 * 1024,
        [QuotaKeys.Users] = 50,
        [QuotaKeys.Applications] = 10,
        [QuotaKeys.EmailsPerDay] = 1000,
        [QuotaKeys.ConcurrentImports] = 2,
        [QuotaKeys.ConcurrentConnectorCalls] = 10,
        [QuotaKeys.ConcurrentQueries] = 10,
        [QuotaKeys.WorkerWeight] = 1,
        [QuotaKeys.AuthoringRequests] = null,
    };

    private readonly IReadOnlyDictionary<string, long?> _values;

    private QuotaDefaults(IReadOnlyDictionary<string, long?> values) => _values = values;

    /// <summary>The default of <paramref name="quota"/>, or <c>null</c> for no limit.</summary>
    /// <exception cref="ArgumentException"><paramref name="quota"/> is not one of <see cref="QuotaKeys.All"/>.</exception>
    public long? this[string quota] =>
        _values.TryGetValue(quota, out var value) ? value : throw new ArgumentException($"'{quota}' is not a quota.", nameof(quota));

    /// <summary>
    /// Reads the <c>Quotas:Defaults</c> section. Without <c>Cell:Id</c> the settings are not read and the built-in values
    /// answer.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// In a cell, a setting is not a quota key, or is not a whole number of zero or more. The message names the setting and
    /// never its value.
    /// </exception>
    public static QuotaDefaults Load(IConfiguration configuration, CellOptions cell)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(cell);

        var values = new Dictionary<string, long?>(BuiltIn, StringComparer.Ordinal);
        if (!cell.IsCell)
        {
            return new QuotaDefaults(values);
        }

        var problems = new List<string>();
        foreach (var setting in configuration.GetSection(SectionName).GetChildren())
        {
            var name = $"{SectionName}:{setting.Key}";
            if (!values.ContainsKey(setting.Key))
            {
                problems.Add($"'{name}' is not a quota. The quotas are {string.Join(", ", QuotaKeys.All)}.");
            }
            else if (!long.TryParse(setting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                problems.Add($"'{name}' must be a whole number of zero or more.");
            }
            else
            {
                values[setting.Key] = value;
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The quota defaults are invalid. {string.Join(" ", problems)}");
        }

        return new QuotaDefaults(values);
    }
}
