using System.Security.Cryptography;
using System.Text;
using Axis.Admin;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant email set</c> and <c>tenant email clear</c> give a directory tenant its own mail settings, with the password stored
/// encrypted, and remove them again.
/// </summary>
public sealed class AdminTenantEmailSetTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_password_is_stored_encrypted_and_no_output_or_record_holds_it_and_the_record_names_the_settings()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.RowAsync(id);
        var password = $"smtp-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

        var result = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", "smtp.tenant.test", "--from", "noreply@tenant.test", "--port", "2525", "--tls", "false", "--user-name", "mailer", "--password"],
            extra: Directory(password, development: true));

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} email ok"], result.Output);
        Assert.Empty(result.Error);
        var row = await scenario.RowAsync(id);
        Assert.Equal("smtp.tenant.test", row.SmtpHost);
        Assert.Equal(2525, row.SmtpPort);
        Assert.False(row.SmtpUseTls);
        Assert.Equal("mailer", row.SmtpUserName);
        Assert.Equal("noreply@tenant.test", row.EmailFrom);
        Assert.Equal(password, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.SmtpPassword, row.SmtpPassword!).Reveal());
        Assert.False(row.SmtpPassword.AsSpan().IndexOf(Encoding.UTF8.GetBytes(password)) >= 0, "The stored SMTP password holds the plain password.");
        Assert.Equal(before.Version + 1, row.Version);
        var records = await scenario.AuditRecordsAsync();
        var record = Assert.Single(records, record => record.Command == "tenant email set");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["smtpHost", "smtpPort", "smtpUseTls", "smtpUserName", "smtpPassword", "from"], record.Changed);
        Assert.All(records, entry => Assert.DoesNotContain(password, entry.Text, StringComparison.Ordinal));
        Assert.DoesNotContain(password, string.Join('\n', [.. result.Output, .. result.Error]), StringComparison.Ordinal);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Without_the_variable_the_password_is_the_first_line_of_standard_input_and_a_second_set_replaces_the_whole_block()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var piped = $"piped-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

        var first = await scenario.RunAsync(
            ["tenant", "email", "set", id, "--host", "smtp.one.test", "--from", "one@tenant.test", "--user-name", "mailer", "--password"],
            extra: _directory,
            input: $"{piped}\r\nignored\n");

        Assert.Equal(0, first.Exit);
        var row = await scenario.RowAsync(id);
        Assert.Equal(piped, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.SmtpPassword, row.SmtpPassword!).Reveal());
        Assert.Null(row.SmtpPort);
        Assert.Null(row.SmtpUseTls);

        // A second set with fewer options resets the options it leaves out, so a new host never inherits the old sign-in.
        var second = await scenario.RunAsync(["tenant", "email", "set", id, "--host", "smtp.two.test", "--from", "two@tenant.test"], extra: _directory);

        Assert.Equal(0, second.Exit);
        var replaced = await scenario.RowAsync(id);
        Assert.Equal("smtp.two.test", replaced.SmtpHost);
        Assert.Equal("two@tenant.test", replaced.EmailFrom);
        Assert.Null(replaced.SmtpUserName);
        Assert.Null(replaced.SmtpPassword);
        Assert.Equal(row.Version + 1, replaced.Version);
        var records = await scenario.AuditRecordsAsync();
        Assert.Equal(["smtpHost", "from"], records[^1].Changed);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_refused_set_exits_with_2_and_changes_neither_the_tenant_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(provisioning, $"{provisioning}.example.test")).Exit);
        await scenario.SetStatusAsync(provisioning, "provisioning");
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;
        string[] set = ["--host", "smtp.tenant.test", "--from", "noreply@tenant.test"];

        var refusals = new[]
        {
            await scenario.RunAsync(["tenant", "email", "set", "t-unknown", .. set], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", provisioning, .. set], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--password"], extra: Directory("a-password")),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--port", "0"], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--port", "65536"], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--tls", "false"], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--user-name", "mailer", "--password"], extra: _directory, input: "\n"),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set]),
            await scenario.RunAsync(["tenant", "email", "clear", id]),
            await scenario.RunAsync(["tenant", "email", "set", id, "--host", "smtp.tenant.test"], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--port", "many"], extra: _directory),
            await scenario.RunAsync(["tenant", "email", "set", id, .. set, "--host", "again"], extra: _directory),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
        });
        Assert.Contains("--tls", Assert.Single(refusals, refusal => refusal.Error.Any(line => line.Contains("only in Development", StringComparison.Ordinal))).Error[0], StringComparison.Ordinal);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Clear_removes_every_setting_raises_the_version_and_leaves_the_catalog_with_no_email_for_the_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(
            0,
            (await scenario.RunAsync(
                ["tenant", "email", "set", id, "--host", "smtp.tenant.test", "--from", "noreply@tenant.test", "--user-name", "mailer", "--password", "--port", "2525", "--tls", "true"],
                extra: Directory("a-password"))).Exit);
        var set = await scenario.RowAsync(id);
        var settings = (await DirectoryTenantCatalog.LoadAsync(scenario.Platform, AdminScenario.Cipher, CancellationToken)).Find(id)!.Email!;
        Assert.Equal(("smtp.tenant.test", 2525, true, "mailer", "noreply@tenant.test"), (settings.Host, settings.Port, settings.UseTls, settings.UserName, settings.From));
        Assert.Equal("a-password", settings.Password!.Reveal());

        var result = await scenario.RunAsync(["tenant", "email", "clear", id], extra: _directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} email ok"], result.Output);
        var row = await scenario.RowAsync(id);
        Assert.Null(row.SmtpHost);
        Assert.Null(row.SmtpPort);
        Assert.Null(row.SmtpUseTls);
        Assert.Null(row.SmtpUserName);
        Assert.Null(row.SmtpPassword);
        Assert.Null(row.EmailFrom);
        Assert.Equal(set.Version + 1, row.Version);
        var records = await scenario.AuditRecordsAsync();
        var record = Assert.Single(records, record => record.Command == "tenant email clear");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["email"], record.Changed);
        Assert.Null((await DirectoryTenantCatalog.LoadAsync(scenario.Platform, AdminScenario.Cipher, CancellationToken)).Find(id)!.Email);
        await scenario.AssertNoSecretsAsync();
    }

    private static Dictionary<string, string?> Directory(string password, bool development = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            [AdminCommands.SmtpPasswordSetting] = password,
        };
        if (development)
        {
            // Without this entry the scenario's default environment, Testing, applies.
            settings[AdminCommands.DotnetEnvironmentSetting] = "Development";
        }

        return settings;
    }
}
