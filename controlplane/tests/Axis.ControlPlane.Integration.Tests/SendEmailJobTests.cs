using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The <c>sendEmail</c> job against a real PostgreSQL and a real Mailpit. Each test has a database of its own, as the other job
/// tests do, and an address of its own on the shared Mailpit.
/// </summary>
public sealed class SendEmailJobTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string MailFrom = "no-reply@vn.example.com";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;
    private readonly EmailTemplates _templates = EmailTemplates.LoadEmbedded();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), TestContext.Current.CancellationToken);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_queued_email_is_sent_once_from_the_control_plane_address_in_the_chosen_language_and_the_job_succeeds()
    {
        var token = TestContext.Current.CancellationToken;
        var address = $"{Guid.NewGuid():N}@example.org";
        var id = await EnqueueAsync(address, "vi");
        var runner = NewRunner(await MailpitOptionsAsync());

        Assert.True(await runner.RunOnceAsync(token));

        var message = Assert.Single(await mailpit.MessagesToAsync(address, token));
        Assert.Equal(MailFrom, message.From);
        Assert.Equal([address], message.To);
        var expected = _templates.Render("tenantReady", "vi", Values());
        Assert.Equal(expected.Subject, message.Subject);
        Assert.Contains("https://acme.vn.example.com", message.Text, StringComparison.Ordinal);
        Assert.Equal($"{id:N}@vn.example.com", message.MessageId);
        Assert.Equal("succeeded", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.False(await runner.RunOnceAsync(token));
        Assert.Single(await mailpit.MessagesToAsync(address, token));
    }

    [Fact]
    public async Task A_send_that_fails_is_queued_again_with_no_address_in_the_error_and_the_next_run_sends_it_once()
    {
        var token = TestContext.Current.CancellationToken;
        var address = $"{Guid.NewGuid():N}@example.org";
        var id = await EnqueueAsync(address, "en");
        var down = await NewRunner(new ControlPlaneMailOptions { Host = "127.0.0.1", Port = 1, UseTls = false }).RunOnceAsync(token);

        Assert.True(down);
        Assert.Equal("queued", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.Equal(1, await ScalarAsync<int>($"SELECT attempts FROM control_plane.jobs WHERE id = '{id}'"));
        var error = await ScalarAsync<string>($"SELECT last_error FROM control_plane.jobs WHERE id = '{id}'");
        Assert.Contains("did not take the email", error, StringComparison.Ordinal);
        Assert.DoesNotContain(address, error, StringComparison.Ordinal);
        Assert.Empty(await mailpit.MessagesToAsync(address, token));

        await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"UPDATE control_plane.jobs SET next_attempt_at = now() WHERE id = '{id}'", token);
        Assert.True(await NewRunner(await MailpitOptionsAsync()).RunOnceAsync(token));

        var message = Assert.Single(await mailpit.MessagesToAsync(address, token));
        Assert.Equal(_templates.Render("tenantReady", "en", Values()).Subject, message.Subject);
        Assert.Equal(2, await ScalarAsync<int>($"SELECT attempts FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.Equal("succeeded", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
    }

    [Fact]
    public async Task An_email_with_an_unknown_template_a_bad_language_a_missing_value_or_the_confirm_template_is_never_queued()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);

        await Assert.ThrowsAsync<ArgumentException>(() => EmailJobs.EnqueueAsync(connection, null, _templates, "nope", "en", "a@example.org", Values(), token));
        await Assert.ThrowsAsync<ArgumentException>(() => EmailJobs.EnqueueAsync(connection, null, _templates, "tenantReady", "fr", "a@example.org", Values(), token));
        await Assert.ThrowsAsync<ArgumentException>(() => EmailJobs.EnqueueAsync(connection, null, _templates, "tenantReady", "en", "a@example.org", new Dictionary<string, string>(), token));
        await Assert.ThrowsAsync<ArgumentException>(() => EmailJobs.EnqueueAsync(connection, null, _templates, "tenantReady", "en", "not an address", Values(), token));

        // The confirm link holds a token, so that template has a job of its own and never a stored payload.
        var confirmValues = new Dictionary<string, string> { ["name"] = "An", ["tenantAddress"] = "https://acme.vn.example.com", ["confirmLink"] = "https://account.vn.example.com/sign-up/verify#token" };
        await Assert.ThrowsAsync<ArgumentException>(() => EmailJobs.EnqueueAsync(connection, null, _templates, "signUpConfirm", "en", "a@example.org", confirmValues, token));

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
    }

    private static Dictionary<string, string> Values() => new() { ["name"] = "An", ["tenantAddress"] = "https://acme.vn.example.com" };

    private async Task<Guid> EnqueueAsync(string address, string language)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return await EmailJobs.EnqueueAsync(connection, null, _templates, "tenantReady", language, address, Values(), TestContext.Current.CancellationToken);
    }

    private async Task<ControlPlaneMailOptions> MailpitOptionsAsync() =>
        new() { Host = await mailpit.SmtpHostAsync(), Port = await mailpit.SmtpPortAsync(), UseTls = false };

    private JobRunner NewRunner(ControlPlaneMailOptions mail)
    {
        var options = new ControlPlaneOptions { MailFrom = MailFrom, Mail = mail };
        return new JobRunner(
            _dataSource,
            [new SendEmailJobHandler(options, _templates)],
            new JobRunnerOptions(),
            new LoggerFactory().CreateLogger<JobRunner>());
    }

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);
}
