using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Tenancy.Keys;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Record imports and inbound endpoints store a sensitive value as ciphertext under the data key of the tenant and read it back
/// in plain text. It runs on the FilesApp fixture in tenant A, where the <c>note</c> field of a claim is <c>sensitive</c>. The host
/// of the fixture holds no data key for tenant A, so a test derives a host that holds one and builds the import handlers with the
/// same key.
/// </summary>
public sealed class RecordImportSensitiveValueTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim";
    private const string Call = "/api/apps/FilesApp/endpoints/ImportClaim";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private WebApplicationFactory<Program> Keyed(string key) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenants:a:DataKeys:1", key);
            builder.UseTestEndpointCallers(new TestIdentity("claims-system", "Claims System", "importer"));
        });

    private static SensitiveValues Sensitive(string key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tenants:a:DataKeys:1"] = key })
            .Build();
        return new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration)));
    }

    private string ClaimTable()
    {
        Assert.True(fixture.FilesModel.TryGetEntity("Claim", out var entity));
        return EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));
    }

    // The imports and the claims of earlier tests are removed, so a test runs only its own dry runs and sees only its own claims.
    private async Task ResetAsync()
    {
        await fixture.ExecuteAsync(TenantA, "DELETE FROM axis.record_imports");
        await fixture.ExecuteAsync(TenantA, $"DELETE FROM axis.process_work_items WHERE kind IN ('{RecordImports.CheckWorkItemKind}', '{RecordImports.CommitWorkItemKind}')");
        await fixture.ExecuteAsync(TenantA, $"DELETE FROM {ClaimTable()}");
    }

    /// <summary>Reads a column as the runtime role: its type name and its bytes.</summary>
    private async Task<(string Type, byte[] Bytes)> StoredAsync(string column, string where)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString(TenantA)));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {EntityNaming.Quote(column)} FROM {ClaimTable()} WHERE {where}", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetDataTypeName(0), reader.GetFieldValue<byte[]>(0));
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string csv, string? key = null)
    {
        using var response = await client.SendAsync(
            Request(HttpMethod.Post, $"{Claims}/imports{(key is null ? "" : $"?key={key}")}", HostA, csv, "text/csv"), CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> StateAsync(HttpClient client, Guid id)
    {
        using var response = await client.SendAsync(Request($"{Claims}/imports/{id:D}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var import = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return import.RootElement.GetProperty("state").GetString()!;
    }

    private async Task RunChecksAsync(SensitiveValues sensitive) =>
        await RunAsync(RecordImports.CheckWorkItemKind, new RecordImportCheckHandler(
            new ReleaseModelCache(PackageCatalog.Empty),
            new RecordImportSettings(10000),
            sensitive,
            NullLoggerFactory.Instance,
            NullLogger<RecordImportCheckHandler>.Instance));

    private async Task RunCommitsAsync(SensitiveValues sensitive) =>
        await RunAsync(RecordImports.CommitWorkItemKind, new RecordImportCommitHandler(
            new ReleaseModelCache(PackageCatalog.Empty),
            sensitive,
            NullLoggerFactory.Instance,
            NullLogger<RecordImportCommitHandler>.Instance));

    // Claims and runs the one work item of the kind that the test queued.
    private async Task RunAsync(string kind, IExternalWorkItemHandler handler)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var item = await WorkItemQueue.ClaimAsync(connection, TenantA, [kind], TimeSpan.FromMinutes(5), CancellationToken);
        Assert.NotNull(item);
        Assert.Equal(WorkItemOutcome.Completed, await handler.ExecuteAsync(item, connection, CancellationToken));
    }

    private static async Task CommitAsync(HttpClient client, Guid id)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, $"{Claims}/imports/{id:D}/commit", HostA, null, null), CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<Guid> CreateClaimAsync(HttpClient client, string values)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, $"{Claims}/records", HostA, $$"""{ "values": {{values}} }"""), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadClaimAsync(HttpClient client, Guid id)
    {
        using var response = await client.SendAsync(Request($"{Claims}/records/{id:D}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.Clone();
    }

    private static string IdWhere(Guid id) => $"{EntityNaming.Quote(EntityNaming.IdColumn)} = '{id:D}'";

    [Fact]
    public async Task An_import_with_a_sensitive_column_commits_it_as_bytes_without_the_value_and_the_export_returns_the_plain_value()
    {
        await ResetAsync();
        var key = NewKey();
        var sensitive = Sensitive(key);
        await using var keyed = Keyed(key);
        using var requester = keyed.CreateClient().SignedInAs("requester");
        const string note = "Handwritten note of Ngô Thị Hạnh";

        var import = await UploadAsync(requester, $"title,note\nImported claim,{note}\n");
        await RunChecksAsync(sensitive);
        Assert.Equal("checked", await StateAsync(requester, import));

        await CommitAsync(requester, import);
        await RunCommitsAsync(sensitive);
        Assert.Equal("committed", await StateAsync(requester, import));

        var (type, bytes) = await StoredAsync(EntityNaming.Column("note"), $"{EntityNaming.Quote(EntityNaming.Column("title"))} = 'Imported claim'");
        Assert.Equal("bytea", type);
        Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(note)) >= 0);

        using var export = await requester.SendAsync(Request($"{Claims}/records/export?format=csv", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains(note, await export.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_import_by_key_updates_a_record_that_holds_a_stored_sensitive_value_in_its_dry_run_and_its_commit()
    {
        await ResetAsync();
        var key = NewKey();
        var sensitive = Sensitive(key);
        await using var keyed = Keyed(key);
        using var requester = keyed.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester, """{ "title": "Keyed claim", "code": "K-1", "note": "Old note" }""");

        var import = await UploadAsync(requester, "code,note\nK-1,New note\n", key: "code");
        await RunChecksAsync(sensitive);
        Assert.Equal("checked", await StateAsync(requester, import));

        await CommitAsync(requester, import);
        await RunCommitsAsync(sensitive);
        Assert.Equal("committed", await StateAsync(requester, import));
        Assert.Equal(
            1L,
            await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.audit_records WHERE action = 'records.imported' AND details = '{\"created\": 0, \"updated\": 1}'::jsonb"));

        var read = await ReadClaimAsync(requester, claim);
        Assert.Equal("New note", read.GetProperty("values").GetProperty("note").GetString());
        Assert.Equal(1L, await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {ClaimTable()} WHERE {EntityNaming.Quote(EntityNaming.Column("code"))} = 'K-1'"));
    }

    [Fact]
    public async Task An_inbound_endpoint_call_that_sets_a_sensitive_value_stores_it_as_bytes_without_the_value()
    {
        await ResetAsync();
        await using var keyed = Keyed(NewKey());
        using var caller = keyed.CreateClient().CallingAs("claims-system");
        using var requester = keyed.CreateClient().SignedInAs("requester");
        const string note = "Called in by Trần Văn Đức";

        using var response = await caller.SendAsync(
            Request(HttpMethod.Post, Call, HostA, $$"""{ "input": { "title": "Endpoint claim", "note": "{{note}}" } }"""), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var called = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var claim = called.RootElement.GetProperty("recordId").GetGuid();
        var (type, bytes) = await StoredAsync(EntityNaming.Column("note"), IdWhere(claim));
        Assert.Equal("bytea", type);
        Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(note)) >= 0);
        var read = await ReadClaimAsync(requester, claim);
        Assert.Equal(note, read.GetProperty("values").GetProperty("note").GetString());
    }
}
