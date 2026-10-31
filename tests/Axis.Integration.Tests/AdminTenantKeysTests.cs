using Axis.Tenancy.Platform;

namespace Axis.Integration.Tests;

/// <summary><c>tenant create</c> gives a tenant data key 1 and lookup key 1, stored encrypted and bound to the tenant and the key.</summary>
public sealed class AdminTenantKeysTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_stores_exactly_data_key_1_and_lookup_key_1_that_decrypt_to_32_bytes_and_differ()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();

        var result = await scenario.CreateAsync(id, $"{id}.example.test");

        Assert.Equal(0, result.Exit);
        var keys = await scenario.KeysAsync(id);
        Assert.Equal([(TenantKeyKinds.Data, 1), (TenantKeyKinds.Lookup, 1)], keys.Select(key => (key.Kind, key.Number)));
        var data = Convert.FromBase64String(AdminScenario.Cipher.Decrypt(id, TenantSecretFields.DataKey(1), keys[0].Key).Reveal());
        var lookup = Convert.FromBase64String(AdminScenario.Cipher.Decrypt(id, TenantSecretFields.LookupKey(1), keys[1].Key).Reveal());
        Assert.Equal(32, data.Length);
        Assert.Equal(32, lookup.Length);
        Assert.NotEqual(data, lookup);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_second_tenant_gets_keys_of_its_own()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var first = scenario.NewTenantId();
        var second = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(first, $"{first}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(second, $"{second}.example.test")).Exit);

        var one = Plain(first, await scenario.KeysAsync(first));
        var two = Plain(second, await scenario.KeysAsync(second));

        Assert.Equal(2, one.Count);
        Assert.Equal(2, two.Count);
        Assert.Empty(one.Intersect(two));
    }

    [Fact]
    public async Task A_stored_key_does_not_decrypt_for_another_tenant_or_another_number_or_the_other_kind()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var keys = await scenario.KeysAsync(id);
        var data = keys.Single(key => key.Kind == TenantKeyKinds.Data).Key;

        Assert.Throws<InvalidOperationException>(() => AdminScenario.Cipher.Decrypt(other, TenantSecretFields.DataKey(1), data));
        Assert.Throws<InvalidOperationException>(() => AdminScenario.Cipher.Decrypt(id, TenantSecretFields.DataKey(2), data));
        Assert.Throws<InvalidOperationException>(() => AdminScenario.Cipher.Decrypt(id, TenantSecretFields.LookupKey(1), data));
    }

    private static List<string> Plain(string id, List<TenantDataKeyRow> keys) =>
        [.. keys.Select(key => AdminScenario.Cipher.Decrypt(
            id,
            key.Kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(key.Number) : TenantSecretFields.LookupKey(key.Number),
            key.Key).Reveal())];
}
