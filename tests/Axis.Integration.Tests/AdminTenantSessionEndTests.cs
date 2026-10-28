using System.Net;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant session end</c> deletes the session rows of one person or of a whole tenant, so the next request on each cookie answers
/// 401, and it writes one platform audit record before it deletes.
/// </summary>
public sealed class AdminTenantSessionEndTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ending_one_person_signs_them_out_and_leaves_other_people_signed_in()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var a = await CreateTenantAsync(scenario);
        await using var server = CreateServer(scenario);
        var anna = await SignInAsync(server, a, "anna");
        var chau = await SignInAsync(server, a, "chau");
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, a, anna));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, a, chau));

        var result = await scenario.RunAsync(["tenant", "session", "end", a, "--user", "anna"], extra: AdminScenario.Directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{a} sessions ok: 1 ended"], result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(server, a, anna));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, a, chau));
        var record = Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant session end");
        Assert.Equal(a, record.TenantId);
        Assert.Equal(["user:anna"], record.Changed);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Ending_all_signs_out_every_person_of_the_tenant_and_changes_no_other_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var a = await CreateTenantAsync(scenario);
        var b = await CreateTenantAsync(scenario);
        await using var server = CreateServer(scenario);
        var annaOnA = await SignInAsync(server, a, "anna");
        var chauOnA = await SignInAsync(server, a, "chau");
        var annaOnB = await SignInAsync(server, b, "anna");

        var result = await scenario.RunAsync(["tenant", "session", "end", a, "--all"], extra: AdminScenario.Directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{a} sessions ok: 2 ended"], result.Output);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(server, a, annaOnA));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(server, a, chauOnA));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, b, annaOnB));
        Assert.Equal(1L, await TenantSessionCountAsync(scenario, b));
        Assert.Equal(0L, await TenantSessionCountAsync(scenario, a));
        var record = Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant session end");
        Assert.Equal(a, record.TenantId);
        Assert.Equal(["all"], record.Changed);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_person_without_sessions_is_not_an_error_and_the_user_id_must_match_exactly()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var a = await CreateTenantAsync(scenario);
        await using var server = CreateServer(scenario);
        var anna = await SignInAsync(server, a, "anna");

        var result = await scenario.RunAsync(["tenant", "session", "end", a, "--user", "Anna"], extra: AdminScenario.Directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{a} sessions ok: 0 ended"], result.Output);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, a, anna));
        Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant session end");
    }

    [Fact]
    public async Task An_unknown_tenant_a_provisioning_tenant_and_a_usage_error_exit_2_and_write_no_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var a = await CreateTenantAsync(scenario);
        var provisioning = await CreateTenantAsync(scenario);
        await scenario.SetStatusAsync(provisioning, "provisioning");
        await using var server = CreateServer(scenario);
        var anna = await SignInAsync(server, a, "anna");
        var records = (await scenario.AuditRecordsAsync()).Count;

        var cases = new (string[] Args, IReadOnlyDictionary<string, string?> Settings)[]
        {
            (["tenant", "session", "end", "no-such-tenant", "--all"], AdminScenario.Directory),
            (["tenant", "session", "end", provisioning, "--all"], AdminScenario.Directory),
            (["tenant", "session", "end", a], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--user", "anna", "--all"], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--all", "--user", "anna"], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--user"], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--user", " "], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--user", "--all"], AdminScenario.Directory),
            (["tenant", "session", "end", "--all"], AdminScenario.Directory),
            (["tenant", "session", "end", a, "--all"], new Dictionary<string, string?> { ["Tenancy:Source"] = "configuration" }),
            (["tenant", "session", "end", a, "--all"], new Dictionary<string, string?>()),
        };
        foreach (var (args, settings) in cases)
        {
            var result = await scenario.RunAsync(args, extra: settings);

            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
        }

        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(server, a, anna));
        await scenario.AssertNoSecretsAsync();
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        return id;
    }

    private static string Host(string id) => $"{id}.example.test";

    private static async Task<long> TenantSessionCountAsync(AdminScenario scenario, string id)
    {
        var row = await scenario.RowAsync(id);
        var password = AdminScenario.Cipher.Decrypt(id, Axis.Tenancy.Platform.TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        await using var connection = new Npgsql.NpgsqlConnection(scenario.TenantConnectionString(id, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM axis.sessions", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static WebApplicationFactory<Program> CreateServer(AdminScenario scenario) =>
        AdminScenario.CreateServer(scenario.Platform).WithWebHostBuilder(builder =>
        {
            // No refresh runs during the test, so the catalog is the one the start loaded.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new SessionStartupFilter()));
        });

    private static async Task<string> SignInAsync(WebApplicationFactory<Program> factory, string tenantId, string user)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/test/session?user={user}", UriKind.Relative));
        request.Headers.Host = Host(tenantId);
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = Session.Scheme + "=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private static async Task<HttpStatusCode> MeAsync(WebApplicationFactory<Program> factory, string tenantId, string cookie)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = Host(tenantId);
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    /// <summary>Answers <c>GET /test/session?user=&lt;user&gt;</c> by signing the user in to the tenant of the request host.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
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
                        var tenantId = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId;
                        var user = context.Request.Query["user"].ToString();
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, user, user, ["employee"]));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
