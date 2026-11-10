using Axis.ControlPlane.Admin;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The commands of <c>Axis.ControlPlane.Admin</c> on a real PostgreSQL, see <c>docs/reference/control-plane.md#operators</c>. One container holds the
/// control plane databases of two regions, <c>vn</c> and <c>th</c>. Every seeded name, business name and email address, and every row of
/// <c>th</c>, holds a marker of ten digits that cannot appear by chance.
/// </summary>
public sealed class ControlPlaneAdminTests : IAsyncLifetime
{
    private const string BadConnectionStringMarker = "h5820417396";

    private static readonly DateTimeOffset _created = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly Guid _vnPending = Guid.NewGuid();
    private readonly Guid _vnVerified = Guid.NewGuid();
    private readonly Guid _vnTrialing = Guid.NewGuid();
    private readonly Guid _vnDeleted = Guid.NewGuid();
    private readonly List<string> _personal = [];
    private readonly List<string> _thMarkers = ["thmarker4829175036"];

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(token);
            foreach (var database in new[] { "cp_vn", "cp_th" })
            {
                await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
                await command.ExecuteNonQueryAsync(token);
            }
        }

        await ControlPlaneDatabase.MigrateAsync(ConnectionString("cp_vn"), token);
        await ControlPlaneDatabase.MigrateAsync(ConnectionString("cp_th"), token);
        await SeedAsync("cp_vn", "vn", "7391046285", _vnPending, _vnVerified, _vnTrialing, _vnDeleted, token);

        var thIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _thMarkers.AddRange(thIds.Select(id => id.ToString()));
        await SeedAsync("cp_th", "th", "4829175036", thIds[0], thIds[1], thIds[2], thIds[3], token);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Sign_ups_list_prints_the_rows_of_the_region_and_no_name_or_email_address()
    {
        var run = await RunAsync(Settings("cp_vn"), "sign-ups", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Error);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("id", lines[0], StringComparison.Ordinal);
        Assert.Contains("subdomain", lines[0], StringComparison.Ordinal);
        Assert.Equal(3, lines.Length);
        var pending = Assert.Single(lines, line => line.Contains(_vnPending.ToString(), StringComparison.Ordinal));
        Assert.Equal(["vnmarker7391046285-pending", "pending", "2026-03-01T10:00:00Z", "2026-03-02T10:00:00Z"], Cells(pending)[1..]);
        var verified = Assert.Single(lines, line => line.Contains(_vnVerified.ToString(), StringComparison.Ordinal));
        Assert.Equal(["vnmarker7391046285-verified", "verified", "2026-03-01T11:00:00Z", "-"], Cells(verified)[1..]);
        Assert.DoesNotContain("2026-03-02T11:00:00Z", run.Output, StringComparison.Ordinal);
        AssertNoOtherRegionAndNoPersonalData(run.Output);
    }

    [Fact]
    public async Task Subscriptions_list_prints_the_rows_of_the_region_and_no_name_or_email_address()
    {
        var run = await RunAsync(Settings("cp_vn"), "subscriptions", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Error);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("id", lines[0], StringComparison.Ordinal);
        Assert.Contains("trial ends", lines[0], StringComparison.Ordinal);
        Assert.Equal(3, lines.Length);
        var trialing = Assert.Single(lines, line => line.Contains(_vnTrialing.ToString(), StringComparison.Ordinal));
        Assert.Equal(["vnmarker7391046285-a", _vnVerified.ToString(), "trialing", "2026-03-15T10:00:00Z", "2026-03-14T10:00:00Z", "2026-03-01T10:00:00Z"], Cells(trialing)[1..]);
        var deleted = Assert.Single(lines, line => line.Contains(_vnDeleted.ToString(), StringComparison.Ordinal));
        Assert.Equal(["vnmarker7391046285-b", _vnVerified.ToString(), "deleted", "2026-03-15T10:00:00Z", "-", "2026-03-01T12:00:00Z"], Cells(deleted)[1..]);
        AssertNoOtherRegionAndNoPersonalData(run.Output);
    }

    [Fact]
    public async Task The_other_region_prints_its_own_rows_only()
    {
        var run = await RunAsync(Settings("cp_th"), "sign-ups", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("thmarker4829175036-pending", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(_vnPending.ToString(), run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(_vnVerified.ToString(), run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_region_exits_2_and_names_the_setting()
    {
        var settings = Settings("cp_vn");
        settings.Remove("ControlPlane:Region");

        var run = await RunAsync(settings, "sign-ups", "list");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Contains("ControlPlane:Region", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sign-ups")]
    [InlineData("subscriptions")]
    public async Task A_wrong_connection_string_exits_2_and_names_the_setting_but_not_its_value(string command)
    {
        var settings = Settings("cp_vn");
        settings["ControlPlane:ConnectionString"] = $"Host={BadConnectionStringMarker};;bogus";

        var run = await RunAsync(settings, command, "list");

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Contains("ControlPlane:ConnectionString", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(BadConnectionStringMarker, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_that_cannot_be_read_exits_1_and_prints_nothing_to_output()
    {
        var settings = Settings("cp_vn");
        settings["ControlPlane:ConnectionString"] = new NpgsqlConnectionStringBuilder(ConnectionString("cp_vn")) { Host = "unreachable5820417396.invalid", Timeout = 2 }.ConnectionString;

        var run = await RunAsync(settings, "sign-ups", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Contains("ControlPlane:ConnectionString", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable5820417396", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sign-ups", "delete")]
    [InlineData("subscriptions")]
    [InlineData("tenants", "list")]
    public async Task An_unknown_command_exits_2_with_the_usage(params string[] args)
    {
        var run = await RunAsync(Settings("cp_vn"), args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.StartsWith("Usage:", run.Error, StringComparison.Ordinal);
    }

    // The columns are padded with at least two spaces between them, and no cell holds two spaces in a row.
    private static string[] Cells(string line) => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void AssertNoOtherRegionAndNoPersonalData(string output)
    {
        foreach (var marker in _thMarkers)
        {
            Assert.DoesNotContain(marker, output, StringComparison.Ordinal);
        }

        foreach (var marker in _personal)
        {
            Assert.DoesNotContain(marker, output, StringComparison.Ordinal);
        }
    }

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;

    private Dictionary<string, string?> Settings(string database) => new()
    {
        ["DOTNET_ENVIRONMENT"] = "Testing",
        ["ControlPlane:Region"] = database == "cp_vn" ? "vn" : "th",
        ["ControlPlane:TenantDomain"] = $"{(database == "cp_vn" ? "vn" : "th")}.example.com",
        ["ControlPlane:ConnectionString"] = ConnectionString(database),
        ["ControlPlane:MailFrom"] = "no-reply@vn.example.com",
        ["ControlPlane:Mail:Host"] = "mail.invalid",
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(Dictionary<string, string?> settings, params string[] args)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ControlPlaneAdminCommands.RunAsync(args, configuration, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), error.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Two sign-ups, one pending and one verified, and two subscriptions of the verified one, one trialing and one deleted.</summary>
    private async Task SeedAsync(string database, string region, string marker, Guid pending, Guid verified, Guid trialing, Guid deleted, CancellationToken cancellationToken)
    {
        await using var context = new ControlPlaneDbContext(new DbContextOptionsBuilder<ControlPlaneDbContext>().UseNpgsql(ConnectionString(database)).Options);
        context.SignUps.AddRange(
            SignUp(pending, $"{region}marker{marker}-pending", "pending", marker + "1", _created),
            SignUp(verified, $"{region}marker{marker}-verified", "verified", marker + "2", _created.AddHours(1)));
        context.Subscriptions.AddRange(
            new SubscriptionRow
            {
                Id = trialing,
                TenantId = $"{region}marker{marker}-a",
                SignUpId = verified,
                State = "trialing",
                TrialEndsAt = _created.AddDays(14),
                NextAt = _created.AddDays(13),
                StepId = Guid.NewGuid(),
                CreatedAt = _created,
            },
            new SubscriptionRow
            {
                Id = deleted,
                TenantId = $"{region}marker{marker}-b",
                SignUpId = verified,
                State = "deleted",
                TrialEndsAt = _created.AddDays(14),
                NextAt = null,
                StepId = Guid.NewGuid(),
                CreatedAt = _created.AddHours(2),
            });
        await context.SaveChangesAsync(cancellationToken);
    }

    private SignUpRow SignUp(Guid id, string subdomain, string state, string marker, DateTimeOffset created)
    {
        var fullName = $"Person {marker}";
        var businessName = $"Business {marker}";
        var email = $"p{marker}@example.test";
        _personal.AddRange([fullName, businessName, email]);

        return new SignUpRow
        {
            Id = id,
            FullName = fullName,
            Email = email,
            NormalisedEmail = email,
            BusinessName = businessName,
            Subdomain = subdomain,
            State = state,
            CreatedAt = created,
            ExpiresAt = created.AddDays(1),
        };
    }
}
