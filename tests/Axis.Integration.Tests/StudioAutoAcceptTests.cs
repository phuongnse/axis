using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using static Axis.Integration.Tests.StudioPublishTests;

namespace Axis.Integration.Tests;

/// <summary>
/// The application setting "Let AI accept its own drafts" lives in the tenant database. Only a publisher changes it,
/// each change is audited, and nothing that writes the application's files can change it.
/// </summary>
[Collection("Test scenario runs")]
public sealed class StudioAutoAcceptTests : IAsyncLifetime
{
    private const string SettingsUrl = "/api/studio/apps/PurchaseRequests/settings";

    private const string ManifestUrl = "/api/studio/apps/PurchaseRequests/resources/219102bc-864f-42ed-874a-c19eaee0d1ba";

    private const string ApplicationId = "219102bc-864f-42ed-874a-c19eaee0d1ba";

    private const string AuditCount =
        "SELECT count(*) FROM axis.audit_records WHERE action = 'application.autoAcceptChanged'";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_setting_is_off_until_a_publisher_turns_it_on_and_an_author_can_read_it()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_default", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var employee = factory.CreateClient().SignedInAs("employee");

        using var read = await GetJsonAsync(employee, SettingsUrl, HostA);

        Assert.False(read.RootElement.GetProperty("autoAccept").GetBoolean());
        Assert.Equal(0L, await SettingRowsAsync(connectionString));
    }

    [Fact]
    public async Task An_author_without_the_publisher_role_cannot_change_it_and_nothing_is_written()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_denied", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var nobody = factory.CreateClient();

        using var refused = await SendAsync(employee, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }""");
        using var signedOut = await SendAsync(nobody, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }""");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("You do not hold the publisher role.", (await ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        using var read = await GetJsonAsync(employee, SettingsUrl, HostA);
        Assert.False(read.RootElement.GetProperty("autoAccept").GetBoolean());
        Assert.Equal(0L, await SettingRowsAsync(connectionString));
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));
    }

    [Fact]
    public async Task A_publishers_change_is_stored_and_audited_once_and_a_repeat_writes_no_record()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_change", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        using var employee = factory.CreateClient().SignedInAs("employee");

        using (var off = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": false }"""))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }

        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));

        using (var on = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }"""))
        {
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
            Assert.True((await ReadAsync(on)).RootElement.GetProperty("autoAccept").GetBoolean());
        }

        using (var read = await GetJsonAsync(employee, SettingsUrl, HostA))
        {
            Assert.True(read.RootElement.GetProperty("autoAccept").GetBoolean());
        }

        Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $$"""
            SELECT count(*) FROM axis.audit_records
            WHERE action = 'application.autoAcceptChanged' AND actor = 'author' AND application_id = '{{ApplicationId}}'
              AND entity_id IS NULL AND record_id IS NULL AND process_instance_id IS NULL
              AND details = '{"autoAccept": true}'::jsonb
            """,
            CancellationToken));

        using (var again = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }"""))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));

        using (var back = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": false }"""))
        {
            Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        }

        Assert.Equal(2L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));
        Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            "SELECT count(*) FROM axis.audit_records WHERE action = 'application.autoAcceptChanged' AND details = '{\"autoAccept\": false}'::jsonb",
            CancellationToken));
        using var final = await GetJsonAsync(employee, SettingsUrl, HostA);
        Assert.False(final.RootElement.GetProperty("autoAccept").GetBoolean());
    }

    [Theory]
    [InlineData("text/plain", """{ "autoAccept": true }""", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("application/json", "not json", HttpStatusCode.BadRequest)]
    [InlineData("application/json", "[true]", HttpStatusCode.BadRequest)]
    [InlineData("application/json", "{}", HttpStatusCode.BadRequest)]
    [InlineData("application/json", """{ "autoAccept": "true" }""", HttpStatusCode.BadRequest)]
    [InlineData("application/json", """{ "autoAccept": null }""", HttpStatusCode.BadRequest)]
    [InlineData("application/json", """{ "autoAccept": true, "other": 1 }""", HttpStatusCode.BadRequest)]
    [InlineData("application/json", """{ "other": true }""", HttpStatusCode.BadRequest)]
    public async Task A_body_that_is_not_one_boolean_is_refused_and_nothing_is_written(string contentType, string body, HttpStatusCode expected)
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_body", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");

        using var refused = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, body, contentType);

        Assert.Equal(expected, refused.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal(
                "The body must be an object with a boolean autoAccept.",
                (await ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(0L, await SettingRowsAsync(connectionString));
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));
    }

    [Fact]
    public async Task An_unknown_application_is_not_found_and_a_manifest_without_an_id_is_off_and_cannot_be_changed()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_manifest", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");

        using var unknown = await SendAsync(author, HttpMethod.Get, "/api/studio/apps/NoSuchApplication/settings", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var manifest = Path.Combine(folder.Path, "application.json");
        var text = await File.ReadAllTextAsync(manifest, CancellationToken);
        await File.WriteAllTextAsync(manifest, text.Replace(ApplicationId, "not-a-uuid", StringComparison.Ordinal), CancellationToken);

        using var read = await GetJsonAsync(author, SettingsUrl, HostA);
        Assert.False(read.RootElement.GetProperty("autoAccept").GetBoolean());
        using var refused = await SendAsync(author, HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }""");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0L, await SettingRowsAsync(connectionString));
    }

    [Fact]
    public async Task No_write_to_the_application_files_and_no_bearer_token_changes_the_setting()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_auto_accept_files", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        using var nobody = factory.CreateClient();

        // The manifest cannot hold the setting: a patch that adds it, and a later changes call, leave it off.
        string version;
        using (var manifest = await SendAsync(author, HttpMethod.Get, ManifestUrl, HostA))
        {
            Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
            version = (await ReadAsync(manifest)).RootElement.GetProperty("version").GetString()!;
        }

        using (var patch = RecordApiFixture.Request(
                   HttpMethod.Patch, ManifestUrl, HostA, """[{ "op": "add", "path": "/autoAccept", "value": true }]""", "application/json-patch+json"))
        {
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var patched = await author.SendAsync(patch, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            version = (await ReadAsync(patched)).RootElement.GetProperty("version").GetString()!;
        }

        using (var changed = await SendAsync(
                   author,
                   HttpMethod.Post,
                   "/api/studio/apps/PurchaseRequests/changes",
                   HostA,
                   $$"""{ "changes": [{ "patch": [{ "op": "replace", "path": "/autoAccept", "value": true }], "id": "{{ApplicationId}}", "version": "{{version}}" }] }"""))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }

        using (var read = await GetJsonAsync(author, SettingsUrl, HostA))
        {
            Assert.False(read.RootElement.GetProperty("autoAccept").GetBoolean());
        }

        Assert.Equal(0L, await SettingRowsAsync(connectionString));

        // A bearer token is no session.
        using var bearer = RecordApiFixture.Request(HttpMethod.Put, SettingsUrl, HostA, """{ "autoAccept": true }""");
        bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-session");
        using var refused = await nobody.SendAsync(bearer, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(0L, await SettingRowsAsync(connectionString));
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, AuditCount, CancellationToken));
    }

    private static async Task<long> SettingRowsAsync(string connectionString) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, "SELECT count(*) FROM axis.application_settings", CancellationToken))!;
}
