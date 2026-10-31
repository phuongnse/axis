using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>The key service gives the newest key of a kind for a write, a key by number for a read, and reads again for a number it lacks.</summary>
public sealed class TenantKeysTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Under_the_directory_source_a_key_added_after_the_first_read_is_found_by_its_number_and_is_the_newest()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var keys = new TenantKeys(new DirectoryTenantKeySource(scenario.Platform, AdminScenario.Cipher));

        var first = await keys.NewestAsync(id, TenantKeyKind.Data, CancellationToken);

        Assert.Equal(1, first!.Number);
        Assert.Equal(32, first.Reveal().Length);
        Assert.Null(await keys.FindAsync(id, TenantKeyKind.Data, 2, CancellationToken));

        var added = new byte[32];
        Array.Fill(added, (byte)9);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantDataKeys.Add(new TenantDataKeyRow
            {
                TenantId = id,
                Kind = TenantKeyKinds.Data,
                Number = 2,
                Key = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.DataKey(2), Convert.ToBase64String(added)),
            });
            await context.SaveChangesAsync(CancellationToken);
        }

        var found = await keys.FindAsync(id, TenantKeyKind.Data, 2, CancellationToken);

        Assert.Equal(added, found!.Reveal().ToArray());
        Assert.Equal(2, (await keys.NewestAsync(id, TenantKeyKind.Data, CancellationToken))!.Number);
        Assert.Equal(1, (await keys.NewestAsync(id, TenantKeyKind.Lookup, CancellationToken))!.Number);
        Assert.Equal(first.Reveal().ToArray(), (await keys.FindAsync(id, TenantKeyKind.Data, 1, CancellationToken))!.Reveal().ToArray());
    }

    [Fact]
    public async Task Under_the_directory_source_a_tenant_without_keys_gives_no_key_and_a_key_copied_to_another_tenant_throws()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(other, $"{other}.example.test")).Exit);
        var keys = new TenantKeys(new DirectoryTenantKeySource(scenario.Platform, AdminScenario.Cipher));

        Assert.Null(await keys.NewestAsync("unknown", TenantKeyKind.Data, CancellationToken));

        var copied = (await scenario.KeysAsync(id)).Single(key => key.Kind == TenantKeyKinds.Data).Key;
        await PostgreSqlServer.AdminScalarAsync(scenario.Platform, $"DELETE FROM platform.tenant_data_keys WHERE tenant_id = '{other}' AND kind = 'data'", CancellationToken);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantDataKeys.Add(new TenantDataKeyRow { TenantId = other, Kind = TenantKeyKinds.Data, Number = 1, Key = copied });
            await context.SaveChangesAsync(CancellationToken);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => keys.NewestAsync(other, TenantKeyKind.Data, CancellationToken));
        Assert.Contains($"'{other}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(TenantSecretFields.DataKey(1), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_the_configuration_source_a_key_added_after_the_first_read_is_found_by_its_number_and_is_the_newest()
    {
        var configuration = new ConfigurationManager();
        configuration["Tenants:t:DataKeys:1"] = Key(1);
        configuration["Tenants:t:LookupKeys:1"] = Key(2);
        var keys = new TenantKeys(new ConfigurationTenantKeySource(configuration));

        Assert.Equal(1, (await keys.NewestAsync("t", TenantKeyKind.Data, CancellationToken))!.Number);
        Assert.Null(await keys.FindAsync("t", TenantKeyKind.Data, 2, CancellationToken));

        configuration["Tenants:t:DataKeys:2"] = Key(3);

        var found = await keys.FindAsync("t", TenantKeyKind.Data, 2, CancellationToken);
        Assert.Equal(Convert.FromBase64String(Key(3)), found!.Reveal().ToArray());
        Assert.Equal(2, (await keys.NewestAsync("t", TenantKeyKind.Data, CancellationToken))!.Number);
        Assert.Equal(1, (await keys.NewestAsync("t", TenantKeyKind.Lookup, CancellationToken))!.Number);
        Assert.Null(await keys.NewestAsync("other", TenantKeyKind.Data, CancellationToken));
    }

    [Theory]
    [InlineData("0", "bad-number")]
    [InlineData("x", "bad-number")]
    [InlineData("1", "not base64!")]
    [InlineData("1", "AAAA")]
    public async Task Under_the_configuration_source_a_malformed_key_throws_naming_the_setting_and_not_the_value(string number, string value)
    {
        var configuration = new ConfigurationManager();
        configuration[$"Tenants:t:DataKeys:{number}"] = number == "1" ? value : Key(1);
        var keys = new TenantKeys(new ConfigurationTenantKeySource(configuration));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => keys.NewestAsync("t", TenantKeyKind.Data, CancellationToken));

        Assert.Contains($"Tenants:t:DataKeys:{number}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key(1), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_shows_only_its_kind_and_number()
    {
        var key = new TenantKey(TenantKeyKind.Lookup, 3, Convert.FromBase64String(Key(7)));

        Assert.Equal("lookup key 3", key.ToString());
    }

    private static string Key(byte fill) => Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());
}
