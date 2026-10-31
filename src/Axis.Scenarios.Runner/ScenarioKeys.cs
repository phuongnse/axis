using System.Security.Cryptography;
using Axis.Tenancy.Keys;

namespace Axis.Scenarios.Runner;

/// <summary>
/// The keys of the scenario tenant: one random data key and one random lookup key, both number 1. They are held in
/// memory for one run and never stored, so a scenario encrypts sensitive values as a real tenant does and nothing
/// outlives the run. Any other tenant has no keys.
/// </summary>
internal sealed class ScenarioKeys : ITenantKeySource
{
    private readonly IReadOnlyList<TenantKey> _keys =
    [
        new TenantKey(TenantKeyKind.Data, 1, RandomNumberGenerator.GetBytes(TenantKey.Size)),
        new TenantKey(TenantKeyKind.Lookup, 1, RandomNumberGenerator.GetBytes(TenantKey.Size)),
    ];

    public Task<IReadOnlyList<TenantKey>> ReadAsync(string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TenantKey>>(string.Equals(tenantId, ScenarioRunner.TenantId, StringComparison.Ordinal) ? _keys : []);
}
