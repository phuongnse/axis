using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The subdomain check of sign-up on a real PostgreSQL. Each test class instance has a container of its own.</summary>
public sealed class SubdomainCheckTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string DirectoryDatabase = "axis_directory";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {DirectoryDatabase}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task The_route_answers_free_taken_reserved_held_and_invalid_for_the_seeded_directory()
    {
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        await SeedAsync();

        (string Name, bool Free, string? Reason)[] cases =
        [
            ("fresh", true, null),
            ("acme", false, "taken"),
            ("beta", false, "taken"),
            ("www", false, "reserved"),
            ("vn-1", false, "reserved"),
            ("th-1", false, "reserved"),
            ("gone", false, "held"),
            ("old", false, "held"),
            ("expired", true, null),
            ("Acme", false, "invalid"),
            ("a_b", false, "invalid"),
            (new string('a', 41), false, "invalid"),
            ("master", false, "invalid"),
        ];

        foreach (var (name, free, reason) in cases)
        {
            var response = await client.GetAsync($"/api/sign-up/subdomains/{name}", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var answer = JsonDocument.Parse(body);
            Assert.True(free == answer.RootElement.GetProperty("free").GetBoolean(), $"free for '{name}'");
            Assert.True(reason == answer.RootElement.GetProperty("reason").GetString(), $"reason for '{name}'");
            Assert.Equal(2, answer.RootElement.EnumerateObject().Count());

            // Nothing but the name asked: not the tenant that owns a host, and not a cell.
            foreach (var seeded in new[] { "acme", "vn-1", "th-1" })
            {
                if (name != seeded)
                {
                    Assert.DoesNotContain(seeded, body, StringComparison.Ordinal);
                }
            }
        }
    }

    private string ControlPlaneConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    private string DirectoryConnectionString =>
        new NpgsqlConnectionStringBuilder(ControlPlaneConnectionString) { Database = DirectoryDatabase, Pooling = false }.ConnectionString;

    private async Task<WebApplicationFactory<Program>> FactoryAsync()
    {
        var host = await mailpit.SmtpHostAsync();
        var port = await mailpit.SmtpPortAsync();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", ControlPlaneConnectionString);
            builder.UseSetting("ControlPlane:DirectoryConnectionString", DirectoryConnectionString);
            builder.UseSetting("ControlPlane:Dns:Provider", "InMemory");
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
        });
    }

    // Run after CreateClient, which starts the host and migrates the directory.
    private async Task SeedAsync()
    {
        await using var connection = new NpgsqlConnection(DirectoryConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES
                ('vn', 'vn.example.com', 'https://api.vn.example.com'),
                ('th', 'th.example.com', 'https://api.th.example.com');
            INSERT INTO directory.cells (id, region_code, internal_url, cap) VALUES
                ('vn-1', 'vn', 'https://vn-1.internal', 100),
                ('th-1', 'th', 'https://th-1.internal', 100);
            INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('acme', 'vn', 'vn-1');
            INSERT INTO directory.hosts (host, tenant_id, cell_id) VALUES ('beta.vn.example.com', 'acme', 'vn-1');
            INSERT INTO directory.held (kind, value, held_until) VALUES
                ('tenant', 'gone', NULL),
                ('host', 'old.vn.example.com', now() + interval '30 days'),
                ('tenant', 'expired', now() - interval '1 day');
            """, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
