using System.Net;
using System.Text;
using System.Xml.Linq;
using Axis.Server.Authentication;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The server keeps its data protection keys in <c>platform.data_protection_keys</c>, encrypted with
/// <c>Platform:SecretKey</c>, so replicas and restarts share one session cookie format.
/// </summary>
public sealed class SharedKeyRingTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_session_cookie_issued_by_one_instance_is_accepted_by_a_second_instance_on_the_same_platform_database()
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        await using var first = CreateFactory(platform);
        await using var second = CreateFactory(platform);

        var cookie = await SignInAsync(first);

        Assert.Equal(HttpStatusCode.OK, await GetMeAsync(second, cookie));
        Assert.Equal(1L, await ScalarAsync(platform, "SELECT count(*) FROM platform.data_protection_keys"));
    }

    [Fact]
    public async Task A_session_made_before_a_server_restart_is_accepted_by_a_new_instance()
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        string cookie;
        await using (var before = CreateFactory(platform))
        {
            cookie = await SignInAsync(before);
        }

        await using var after = CreateFactory(platform);

        Assert.Equal(HttpStatusCode.OK, await GetMeAsync(after, cookie));
        Assert.Equal(1L, await ScalarAsync(platform, "SELECT count(*) FROM platform.data_protection_keys"));
    }

    [Fact]
    public async Task The_stored_keys_hold_no_plain_key_material_and_decrypt_with_the_installation_key_bound_to_their_id()
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        await using (var factory = CreateFactory(platform))
        {
            await SignInAsync(factory);
        }

        var rows = await ReadKeyRowsAsync(platform);
        var cipher = TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey })
            .Build());

        Assert.NotEmpty(rows);
        foreach (var (id, stored) in rows)
        {
            var text = Encoding.UTF8.GetString(stored);
            Assert.DoesNotContain("<key", text, StringComparison.Ordinal);
            Assert.DoesNotContain("masterKey", text, StringComparison.Ordinal);

            var plain = cipher.DecryptDataProtectionKey(id, stored).Reveal();
            var key = XElement.Parse(plain);
            Assert.Equal(id, Guid.Parse((string)key.Attribute("id")!));

            var masterKey = key.Descendants().First(element => element.Name.LocalName == "value").Value;
            Assert.DoesNotContain(masterKey, text, StringComparison.Ordinal);
            Assert.DoesNotContain(masterKey, Convert.ToBase64String(stored), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_server_with_another_secret_key_on_a_platform_database_that_holds_a_key_fails_to_start_naming_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        await using (var first = CreateFactory(platform))
        {
            await SignInAsync(first);
        }

        var otherKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, TenantSecretCipher.KeySize).ToArray());
        await using var second = CreateFactory(platform, (TenantSecretCipher.KeySetting, otherKey));

        var exception = Assert.ThrowsAny<Exception>(() => second.CreateClient());

        Assert.Contains("Platform:SecretKey", exception.ToString(), StringComparison.Ordinal);
    }

    // The tenant stays on the class database. Only the platform database is a separate one.
    private WebApplicationFactory<Program> CreateFactory(string platformSchemaConnectionString, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platformSchemaConnectionString);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });

    /// <summary>Signs anna in on the server and returns the value of the session cookie.</summary>
    private static async Task<string> SignInAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync(new Uri("/test/session", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = Session.Scheme + "=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private static async Task<HttpStatusCode> GetMeAsync(WebApplicationFactory<Program> factory, string sessionCookie)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"{Session.Scheme}={sessionCookie}");
        return (await client.SendAsync(request, CancellationToken)).StatusCode;
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    private static async Task<List<(Guid Id, byte[] Xml)>> ReadKeyRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, xml FROM platform.data_protection_keys", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(Guid, byte[])>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetGuid(0), (byte[])reader[1]));
        }

        return rows;
    }

    /// <summary>Answers <c>GET /test/session</c> by signing anna in to the default tenant.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session")
                {
                    await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("default", "anna", "Anna Employee", ["employee"]));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
