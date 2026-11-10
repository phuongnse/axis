using System.Text.Json.Nodes;
using Axis.ControlPlane.Admin;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The commands of <c>Axis.ControlPlane.Admin</c> on a real PostgreSQL and a real Mailpit, see <c>docs/reference/control-plane.md#operators</c>. One container holds the
/// control plane databases of two regions, <c>vn</c> and <c>th</c>. Every seeded name, business name and email address, and every row of
/// <c>th</c>, holds a marker of ten digits that cannot appear by chance.
/// </summary>
public sealed class ControlPlaneAdminTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string BadConnectionStringMarker = "h5820417396";

    private const string Operator = "ops-6203918475";

    private const string TrialingTenant = "vnmarker7391046285-a";

    private static readonly DateTimeOffset _created = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly Guid _vnPending = Guid.NewGuid();
    private readonly Guid _vnVerified = Guid.NewGuid();
    private readonly Guid _vnTrialing = Guid.NewGuid();
    private readonly Guid _vnDeleted = Guid.NewGuid();
    private readonly List<string> _personal = [];
    private NpgsqlDataSource _vnDataSource = null!;
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
        _vnDataSource = NpgsqlDataSource.Create(ConnectionString("cp_vn"));
        await SeedAsync("cp_vn", "vn", "7391046285", _vnPending, _vnVerified, _vnTrialing, _vnDeleted, token);

        var thIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _thMarkers.AddRange(thIds.Select(id => id.ToString()));
        await SeedAsync("cp_th", "th", "4829175036", thIds[0], thIds[1], thIds[2], thIds[3], token);
    }

    public async ValueTask DisposeAsync()
    {
        await _vnDataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

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

    [Fact]
    public async Task Trial_extend_moves_the_trial_end_and_the_next_date_and_writes_one_operator_audit_record()
    {
        var token = TestContext.Current.CancellationToken;
        var stepId = await ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{_vnTrialing}'", token);

        var run = await RunAsync(Settings("cp_vn"), "trial", "extend", TrialingTenant, "--days", "10", "--operator", Operator);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Error);
        Assert.Equal($"{_vnTrialing}  trial ends 2026-03-25T10:00:00Z  next 2026-03-24T10:00:00Z", run.Output.Trim());
        Assert.Equal(_created.AddDays(24), await TrialEndsAtAsync(_vnTrialing, token));
        Assert.Equal(_created.AddDays(23), await NextAtAsync(_vnTrialing, token));
        Assert.Equal(stepId, await ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{_vnTrialing}'", token));
        var record = Assert.Single(await AuditAsync(_vnTrialing.ToString(), token));
        Assert.Equal(("operator", Operator, "subscription.trialExtended"), (record.ActorKind, record.Actor, record.Action));
    }

    [Theory]
    [InlineData("readOnly", TrialingTenant)]
    [InlineData("deleted", "vnmarker7391046285-b")]
    public async Task Trial_extend_refuses_a_subscription_that_is_not_trialing_and_changes_nothing(string state, string tenant)
    {
        var token = TestContext.Current.CancellationToken;
        await ExecuteAsync($"UPDATE control_plane.subscriptions SET state = '{state}' WHERE tenant_id = '{tenant}'", token);

        var run = await RunAsync(Settings("cp_vn"), "trial", "extend", tenant, "--days", "10", "--operator", Operator);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Contains(tenant, run.Error, StringComparison.Ordinal);
        Assert.Equal(_created.AddDays(14), await TrialEndsAtAsync(_vnTrialing, token));
        Assert.Equal(_created.AddDays(13), await NextAtAsync(_vnTrialing, token));
        Assert.Equal(_created.AddDays(14), await TrialEndsAtAsync(_vnDeleted, token));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records", token));
    }

    [Fact]
    public async Task Trial_extend_refuses_a_tenant_with_no_subscription_and_changes_nothing()
    {
        var token = TestContext.Current.CancellationToken;

        var run = await RunAsync(Settings("cp_vn"), "trial", "extend", "vnmarker7391046285-none", "--days", "10", "--operator", Operator);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(_created.AddDays(14), await TrialEndsAtAsync(_vnTrialing, token));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records", token));
    }

    [Fact]
    public async Task Email_resend_sends_the_email_again_through_Mailpit_and_writes_one_email_resent_record()
    {
        var token = TestContext.Current.CancellationToken;
        var address = $"{Guid.NewGuid():N}@example.org";
        var jobId = await EnqueueEmailAsync(address, token);
        var runner = NewRunner(await MailpitOptionsAsync());
        Assert.True(await runner.RunOnceAsync(token));
        Assert.Single(await mailpit.MessagesToAsync(address, token));

        var run = await RunAsync(Settings("cp_vn"), "email", "resend", jobId.ToString(), "--operator", Operator);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Error);
        Assert.StartsWith($"Queued the email of job {jobId} again as job ", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(address, run.Output, StringComparison.Ordinal);
        Assert.True(await runner.RunOnceAsync(token));
        var messages = await mailpit.MessagesToAsync(address, token);
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages.Select(message => message.MessageId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'", token));
        var record = Assert.Single(await AuditAsync(jobId.ToString(), token));
        Assert.Equal(("operator", Operator, "email.resent"), (record.ActorKind, record.Actor, record.Action));
    }

    [Theory]
    [InlineData("advanceSubscriptions")]
    [InlineData("sendEmail")]
    [InlineData("missing")]
    public async Task Email_resend_refuses_a_job_that_is_not_sendEmail_or_has_not_ended_or_does_not_exist(string job)
    {
        var token = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        if (job != "missing")
        {
            await using var connection = await _vnDataSource.OpenConnectionAsync(token);
            jobId = job == EmailJobs.Kind
                ? await EmailJobs.EnqueueAsync(connection, null, EmailTemplates.LoadEmbedded(), "tenantReady", "en", $"{Guid.NewGuid():N}@example.org", EmailValues(), token)
                : await JobQueue.EnqueueAsync(connection, null, job, [], null, token);
        }

        var run = await RunAsync(Settings("cp_vn"), "email", "resend", jobId.ToString(), "--operator", Operator);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.NotEqual("", run.Error);
        Assert.Equal(job == "missing" ? 0 : 1, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs", token));
        Assert.Empty(await AuditAsync(jobId.ToString(), token));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records", token));
    }

    [Theory]
    [InlineData("trial extend vnmarker7391046285-a --days 10")]
    [InlineData("trial extend vnmarker7391046285-a --days 10 --operator <blank>")]
    [InlineData("trial extend vnmarker7391046285-a --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days 0 --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days 366 --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days x --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days -3 --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days 10 --days 10 --operator ops-6203918475")]
    [InlineData("trial extend vnmarker7391046285-a --days 10 --operator ops-6203918475 --force yes")]
    [InlineData("email resend {job}")]
    [InlineData("email resend {job} --operator <blank>")]
    [InlineData("email resend not-an-id --operator ops-6203918475")]
    public async Task A_change_command_without_an_operator_or_with_bad_options_exits_2_with_the_usage_and_changes_nothing(string line)
    {
        var token = TestContext.Current.CancellationToken;
        var jobId = await EnqueueEmailAsync($"{Guid.NewGuid():N}@example.org", token);
        await ExecuteAsync($"UPDATE control_plane.jobs SET state = 'succeeded' WHERE id = '{jobId}'", token);
        var args = line.Replace("{job}", jobId.ToString(), StringComparison.Ordinal).Split(' ').Select(arg => arg == "<blank>" ? "  " : arg).ToArray();

        var run = await RunAsync(Settings("cp_vn"), args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.StartsWith("Usage:", run.Error, StringComparison.Ordinal);
        Assert.Equal(_created.AddDays(14), await TrialEndsAtAsync(_vnTrialing, token));
        Assert.Equal(_created.AddDays(13), await NextAtAsync(_vnTrialing, token));
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs", token));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records", token));
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

    private static Dictionary<string, string> EmailValues() => new() { ["name"] = "An", ["tenantAddress"] = "https://acme.vn.example.com" };

    private async Task<Guid> EnqueueEmailAsync(string address, CancellationToken cancellationToken)
    {
        await using var connection = await _vnDataSource.OpenConnectionAsync(cancellationToken);
        return await EmailJobs.EnqueueAsync(connection, null, EmailTemplates.LoadEmbedded(), "tenantReady", "en", address, EmailValues(), cancellationToken);
    }

    private async Task<ControlPlaneMailOptions> MailpitOptionsAsync() =>
        new() { Host = await mailpit.SmtpHostAsync(), Port = await mailpit.SmtpPortAsync(), UseTls = false };

    private JobRunner NewRunner(ControlPlaneMailOptions mail)
    {
        var options = new ControlPlaneOptions { MailFrom = "no-reply@vn.example.com", Mail = mail };
        return new JobRunner(
            _vnDataSource,
            [new SendEmailJobHandler(options, EmailTemplates.LoadEmbedded())],
            new JobRunnerOptions(),
            new LoggerFactory().CreateLogger<JobRunner>());
    }

    private async Task<IReadOnlyList<AuditRecord>> AuditAsync(string subject, CancellationToken cancellationToken)
    {
        await using var connection = await _vnDataSource.OpenConnectionAsync(cancellationToken);
        return await AuditRecords.ReadAsync(connection, subject, cancellationToken);
    }

    private async Task<DateTimeOffset> TrialEndsAtAsync(Guid id, CancellationToken cancellationToken) =>
        new DateTimeOffset(await ScalarAsync<DateTime>($"SELECT trial_ends_at FROM control_plane.subscriptions WHERE id = '{id}'", cancellationToken));

    private async Task<DateTimeOffset> NextAtAsync(Guid id, CancellationToken cancellationToken) =>
        new DateTimeOffset(await ScalarAsync<DateTime>($"SELECT next_at FROM control_plane.subscriptions WHERE id = '{id}'", cancellationToken));

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken) =>
        await ControlPlaneDatabase.ScalarAsync<T>(_vnDataSource, sql, cancellationToken);

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken) =>
        await ControlPlaneDatabase.ExecuteAsync(_vnDataSource, sql, cancellationToken);

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
