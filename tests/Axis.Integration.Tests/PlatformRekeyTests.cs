using System.Net;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>platform rekey</c> re-encrypts every stored secret with <c>Platform:SecretKey</c>, reading the old values with
/// <c>Platform:PreviousSecretKey</c>, so the installation key rotates with nobody signed out.
/// </summary>
public sealed class PlatformRekeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string BindingName = "PurchasingSystemToken";
    private const string BindingValue = "binding-secret-value";

    private static readonly string _newKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, TenantSecretCipher.KeySize).ToArray());
    private static readonly string _thirdKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, TenantSecretCipher.KeySize).ToArray());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Rotation => new()
    {
        [TenantSecretCipher.KeySetting] = _newKey,
        [TenantSecretCipher.PreviousKeySetting] = TestPlatform.SecretKey,
    };

    [Fact]
    public async Task Every_bytea_column_of_the_platform_model_is_re_encrypted_so_the_new_key_alone_decrypts_it()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await SeedAsync(scenario);
        var columns = EncryptedColumns();

        // A column that the seed leaves empty fails here, so a new encrypted column cannot go unchecked.
        foreach (var (table, column) in columns)
        {
            Assert.True(await ScalarAsync(scenario.Platform, $"SELECT count(*) FROM platform.{table} WHERE length({column}) > 0") is > 0L, $"{table}.{column} is not seeded.");
        }

        var before = await SnapshotAsync(scenario.Platform, columns);
        var versionBefore = await VersionAsync(scenario.Platform, id);
        var newCipher = CipherFor(_newKey);

        var result = await scenario.RunAsync(["platform", "rekey"], extra: Rotation);

        Assert.Equal(0, result.Exit);
        var after = await SnapshotAsync(scenario.Platform, columns);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (field, stored) in before)
        {
            Assert.NotEqual(stored, after[field]);
            Assert.Equal(
                Decrypt(CipherFor(TestPlatform.SecretKey), field, stored),
                Decrypt(newCipher, field, after[field]));
        }

        Assert.Equal(versionBefore + 1, await VersionAsync(scenario.Platform, id));
        Assert.Equal(["platform rekey ok: 9 tenant secrets, 2 data protection keys"], result.Output);
        await scenario.AssertNoSecretsAsync(CipherFor(_newKey, TestPlatform.SecretKey));
    }

    [Fact]
    public async Task Each_realm_client_secret_is_bound_to_its_own_field_name()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await SeedAsync(scenario);

        var result = await scenario.RunAsync(["platform", "rekey"], extra: Rotation);

        Assert.Equal(0, result.Exit);
        await using var context = NewContext(scenario.Platform);
        var tenant = await context.Tenants.AsNoTracking().SingleAsync(row => row.Id == id, CancellationToken);
        var newCipher = CipherFor(_newKey);
        Assert.Equal("realm-activation-secret-value", newCipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, tenant.RealmActivationClientSecret!).Reveal());
        Assert.Equal("realm-users-secret-value", newCipher.Decrypt(id, TenantSecretFields.RealmUsersClientSecret, tenant.RealmUsersClientSecret!).Reveal());
        Assert.Throws<InvalidOperationException>(() => newCipher.Decrypt(id, TenantSecretFields.RealmUsersClientSecret, tenant.RealmActivationClientSecret!));
        Assert.Throws<InvalidOperationException>(() => newCipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, tenant.RealmUsersClientSecret!));
    }

    [Fact]
    public async Task A_value_under_neither_key_exits_2_naming_the_tenant_and_field_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await SeedAsync(scenario);
        var columns = EncryptedColumns();
        await using (var connection = new NpgsqlConnection(scenario.Platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand("UPDATE platform.tenant_bindings SET secret_value = @value WHERE tenant_id = @id", connection);
            command.Parameters.AddWithValue("value", CipherFor(_thirdKey).Encrypt(id, TenantSecretFields.Binding(BindingName), BindingValue));
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var values = await SnapshotAsync(scenario.Platform, columns);
        var version = await VersionAsync(scenario.Platform, id);
        var records = await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records");

        var result = await scenario.RunAsync(["platform", "rekey"], extra: Rotation);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains($"'{id}'", result.Error[0], StringComparison.Ordinal);
        Assert.Contains(TenantSecretFields.Binding(BindingName), result.Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain(BindingValue, result.Error[0], StringComparison.Ordinal);
        Assert.Equal(values, await SnapshotAsync(scenario.Platform, columns));
        Assert.Equal(version, await VersionAsync(scenario.Platform, id));
        Assert.Equal(records, await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_previous_key_the_command_exits_2_and_changes_nothing(string? previous)
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await SeedAsync(scenario);
        var columns = EncryptedColumns();
        var values = await SnapshotAsync(scenario.Platform, columns);
        var version = await VersionAsync(scenario.Platform, id);
        var records = await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records");

        var result = await scenario.RunAsync(
            ["platform", "rekey"],
            extra: new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = _newKey, [TenantSecretCipher.PreviousKeySetting] = previous });

        Assert.Equal(2, result.Exit);
        Assert.Equal(["The setting Platform:PreviousSecretKey is required."], result.Error);
        Assert.Equal(values, await SnapshotAsync(scenario.Platform, columns));
        Assert.Equal(version, await VersionAsync(scenario.Platform, id));
        Assert.Equal(records, await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records"));
    }

    [Fact]
    public async Task A_malformed_previous_key_exits_2_naming_the_setting_and_not_the_value()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await SeedAsync(scenario);
        var columns = EncryptedColumns();
        var values = await SnapshotAsync(scenario.Platform, columns);

        var result = await scenario.RunAsync(
            ["platform", "rekey"],
            extra: new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = _newKey, [TenantSecretCipher.PreviousKeySetting] = "not base64!" });

        Assert.Equal(2, result.Exit);
        Assert.Contains("Platform:PreviousSecretKey", result.Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain("not base64!", result.Error[0], StringComparison.Ordinal);
        Assert.Equal(values, await SnapshotAsync(scenario.Platform, columns));
        Assert.Equal(1L, await ScalarAsync(scenario.Platform, $"SELECT count(*) FROM platform.tenants WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_usage_error_exits_2_and_a_successful_run_writes_exactly_one_record_with_no_tenant_and_only_the_counts()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await SeedAsync(scenario);
        var records = (long)(await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records"))!;

        var usage = await scenario.RunAsync(["platform", "rekey", "extra"], extra: Rotation);
        var result = await scenario.RunAsync(["platform", "rekey"], extra: Rotation);

        Assert.Equal(2, usage.Exit);
        Assert.StartsWith("Usage:", usage.Error[0], StringComparison.Ordinal);
        Assert.Equal(0, result.Exit);
        Assert.Equal(records + 1, await ScalarAsync(scenario.Platform, "SELECT count(*) FROM platform.audit_records"));
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, os_user, machine, changed FROM platform.audit_records WHERE command = 'platform rekey'",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("platform rekey", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(Environment.UserName, reader.GetString(2));
        Assert.Equal(Environment.MachineName, reader.GetString(3));
        Assert.Equal(["tenant secrets: 9", "data protection keys: 2"], (string[])reader[4]);
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task A_session_and_a_secret_binding_keep_working_across_both_keys_the_rekey_and_the_new_key_alone()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";
        Assert.Equal(0, (await scenario.CreateAsync(id, host)).Exit);
        await AddBindingAsync(scenario, id);

        string cookie;
        await using (var before = CreateServer(scenario.Platform, TestPlatform.SecretKey, previous: null, id))
        {
            cookie = await SignInAsync(before, host);
            await AssertWorksAsync(before, id, host, cookie);
        }

        await using (var both = CreateServer(scenario.Platform, _newKey, TestPlatform.SecretKey, id))
        {
            await AssertWorksAsync(both, id, host, cookie);
        }

        Assert.Equal(0, (await scenario.RunAsync(["platform", "rekey"], extra: Rotation)).Exit);

        await using (var after = CreateServer(scenario.Platform, _newKey, previous: null, id))
        {
            await AssertWorksAsync(after, id, host, cookie);
        }
    }

    private static async Task AssertWorksAsync(WebApplicationFactory<Program> factory, string id, string host, string cookie)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = factory.Services.GetRequiredService<ITenantCatalog>().Find(id)!;
        Assert.Equal(BindingValue, entry.Bindings[BindingName].Secret!.Reveal());
    }

    private static async Task<string> SignInAsync(WebApplicationFactory<Program> factory, string host)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/test/session", UriKind.Relative));
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = Session.Scheme + "=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private static WebApplicationFactory<Program> CreateServer(string platform, string key, string? previous, string tenantId) =>
        AdminScenario.CreateServer(platform).WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TenantSecretCipher.KeySetting, key);
            if (previous is not null)
            {
                builder.UseSetting(TenantSecretCipher.PreviousKeySetting, previous);
            }

            // No refresh runs during the test, so the catalog is the one the start loaded.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new SessionStartupFilter(tenantId)));
        });

    /// <summary>Seeds a main tenant with every encrypted field, a <c>secret</c> binding and two data protection keys under the old key.</summary>
    private static async Task<string> SeedAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var cipher = AdminScenario.Cipher;
        await using var context = NewContext(scenario.Platform);
        var tenant = await context.Tenants.SingleAsync(row => row.Id == id, CancellationToken);
        tenant.OidcClientKeyId = "key-current";
        tenant.OidcClientKey = cipher.Encrypt(id, TenantSecretFields.OidcClientKey, "pem-of-the-current-key");
        tenant.OidcPreviousClientKeyId = "key-previous";
        tenant.OidcPreviousClientKey = cipher.Encrypt(id, TenantSecretFields.OidcClientKey, "pem-of-the-previous-key");
        tenant.SmtpHost = "smtp.example.test";
        tenant.EmailFrom = "noreply@example.test";
        tenant.SmtpUserName = "mailer";
        tenant.SmtpPassword = cipher.Encrypt(id, TenantSecretFields.SmtpPassword, "smtp-password-value");
        tenant.RealmActivationClientSecret = cipher.Encrypt(id, TenantSecretFields.RealmActivationClientSecret, "realm-activation-secret-value");
        tenant.RealmUsersClientSecret = cipher.Encrypt(id, TenantSecretFields.RealmUsersClientSecret, "realm-users-secret-value");
        context.TenantBindings.Add(new TenantBindingRow
        {
            TenantId = id,
            Name = BindingName,
            Kind = "secret",
            SecretValue = cipher.Encrypt(id, TenantSecretFields.Binding(BindingName), BindingValue),
        });
        context.TenantBindings.Add(new TenantBindingRow { TenantId = id, Name = "PurchasingSystemUrl", Kind = "url", Url = "https://purchasing.example.test" });
        for (var i = 0; i < 2; i++)
        {
            var key = Guid.NewGuid();
            context.DataProtectionKeys.Add(new DataProtectionKeyRow
            {
                Id = key,
                FriendlyName = $"key-{key}",
                Xml = cipher.EncryptDataProtectionKey(key, $"<key id=\"{key}\"><masterKey>plain-key-material</masterKey></key>"),
            });
        }

        await context.SaveChangesAsync(CancellationToken);
        return id;
    }

    private static async Task AddBindingAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario.Platform);
        context.TenantBindings.Add(new TenantBindingRow
        {
            TenantId = id,
            Name = BindingName,
            Kind = "secret",
            SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(BindingName), BindingValue),
        });
        await context.SaveChangesAsync(CancellationToken);
    }

    /// <summary>Every <c>bytea</c> column of the platform model, found from the model and not from a list.</summary>
    private static List<(string Table, string Column)> EncryptedColumns()
    {
        using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=localhost").Options);
        var columns = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Where(property => property.GetColumnType() == "bytea")
                .Select(property => (entity.GetTableName()!, property.GetColumnName())))
            .Order()
            .ToList();
        Assert.NotEmpty(columns);
        return columns;
    }

    /// <summary>Every non-empty value of the columns, by column and row. A row has the id of its tenant, binding or key.</summary>
    private static async Task<Dictionary<string, byte[]>> SnapshotAsync(string platform, List<(string Table, string Column)> columns)
    {
        var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        foreach (var (table, column) in columns)
        {
            var row = table switch
            {
                "tenants" => "id",
                "tenant_bindings" => "tenant_id || '|' || name",
                "data_protection_keys" => "id::text",
                _ => throw new InvalidOperationException($"The table '{table}' has an encrypted column and this test does not know its row."),
            };
            await using var command = new NpgsqlCommand($"SELECT {row}, {column} FROM platform.{table} WHERE length({column}) > 0", connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            while (await reader.ReadAsync(CancellationToken))
            {
                values[$"{table}.{column}:{reader.GetString(0)}"] = (byte[])reader[1];
            }
        }

        return values;
    }

    /// <summary>Decrypts a value by the table and column of its key, with the tenant, field or key id that the cipher binds to it.</summary>
    private static string Decrypt(TenantSecretCipher cipher, string field, byte[] stored)
    {
        var colon = field.IndexOf(':', StringComparison.Ordinal);
        var column = field[..colon];
        var row = field[(colon + 1)..];
        return column switch
        {
            "tenants.runtime_password" => cipher.Decrypt(row, TenantSecretFields.RuntimePassword, stored).Reveal(),
            "tenants.schema_password" => cipher.Decrypt(row, TenantSecretFields.SchemaPassword, stored).Reveal(),
            "tenants.oidc_client_secret" => cipher.Decrypt(row, TenantSecretFields.OidcClientSecret, stored).Reveal(),
            "tenants.smtp_password" => cipher.Decrypt(row, TenantSecretFields.SmtpPassword, stored).Reveal(),
            "tenants.realm_activation_client_secret" => cipher.Decrypt(row, TenantSecretFields.RealmActivationClientSecret, stored).Reveal(),
            "tenants.realm_users_client_secret" => cipher.Decrypt(row, TenantSecretFields.RealmUsersClientSecret, stored).Reveal(),
            "tenants.oidc_client_key" or "tenants.oidc_previous_client_key" => cipher.Decrypt(row, TenantSecretFields.OidcClientKey, stored).Reveal(),
            "tenant_bindings.secret_value" => cipher.Decrypt(row.Split('|')[0], TenantSecretFields.Binding(row.Split('|')[1]), stored).Reveal(),
            "data_protection_keys.xml" => cipher.DecryptDataProtectionKey(Guid.Parse(row), stored).Reveal(),
            _ => throw new InvalidOperationException($"The column '{column}' is encrypted and this test does not know how to decrypt it."),
        };
    }

    private static TenantSecretCipher CipherFor(string key, string? previous = null) =>
        TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TenantSecretCipher.KeySetting] = key,
                [TenantSecretCipher.PreviousKeySetting] = previous,
            })
            .Build());

    private static async Task<long> VersionAsync(string platform, string id) =>
        (long)(await ScalarAsync(platform, $"SELECT version FROM platform.tenants WHERE id = '{id}'"))!;

    private static async Task<object?> ScalarAsync(string platform, string sql)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    private static PlatformDbContext NewContext(string platform) => new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);

    /// <summary>Answers <c>GET /test/session</c> by signing requester in to the tenant.</summary>
    private sealed class SessionStartupFilter(string tenantId) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, "requester", "Requester User", ["employee"]));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
