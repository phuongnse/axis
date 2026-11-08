using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Studio;
using Axis.Tenancy.Platform;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// An <c>accept</c> request applies a person's draft to the application folder as one revision, see
/// <see href="docs/reference/revisions-and-drafts.md#accepting-a-draft">accepting a draft</see>. These tests run the publisher on real
/// PostgreSQL with a Studio folder and its revisions repository.
/// </summary>
public sealed class PublisherAcceptTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"Accept{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_accept_writes_the_drafts_files_and_adds_one_revision_naming_the_request()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order());
        using var revisions = new TemporaryFolder();
        var author = NewAuthor(scenario, folder.Path, revisions.Path, "author");
        var draft = await NewDraftAsync(author);
        var invoice = Entity(Guid.NewGuid(), "Invoice");
        var written = Written(await author.Drafts.WriteAsync(draft, invoice, CancellationToken));
        var expected = StudioWrites.Serialize(invoice);
        var revisionCount = author.Revisions.List().Count;
        var request = await QueueAsync(scenario, tenant, draft, "author");

        var result = await RunPublisherAsync(scenario, tenant, revisions.Path, folder.Path, request);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await StatusAsync(scenario, tenant, request));
        Assert.Equal([$"{tenant} claim ok", $"{tenant} draft ok", $"{tenant} version ok"], result.Output.Take(3));
        Assert.Equal($"{tenant} accept ok", result.Output[^1]);
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(folder.Path, written.File)));
        var list = author.Revisions.List();
        Assert.Equal(revisionCount + 1, list.Count);
        Assert.Equal("Accept draft", list[0].Summary);
        Assert.Equal("author", list[0].UserId);
        Assert.Equal("agent", list[0].Client);
        Assert.Equal(request.ToString("D"), list[0].Request);
        // The user has no profile, so the author is the user id.
        Assert.Equal("author", list[0].Author);
        using (var repository = new Repository(StudioRevisions.RepositoryPath(revisions.Path, folder.Path)!))
        {
            Assert.Null(repository.Refs["refs/heads/drafts/" + draft]);
            Assert.Null(repository.Refs["refs/axis/drafts/" + draft]);
        }

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task An_accept_after_a_resource_the_draft_read_changed_on_main_fails_with_AXC0377_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order());
        using var revisions = new TemporaryFolder();
        var author = NewAuthor(scenario, folder.Path, revisions.Path, "author");
        var draft = await NewDraftAsync(author);
        Assert.Null((await author.Drafts.ReadAsync(draft, _orderId, CancellationToken)).Refusal);
        Written(await author.Drafts.WriteAsync(draft, Entity(Guid.NewGuid(), "Invoice"), CancellationToken));
        folder.With("entities/order.json", Order("remark"));
        author.Revisions.RecordOutside();
        var folderBefore = Snapshot(folder.Path);
        var tip = MainTip(revisions.Path, folder.Path);
        var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder.Path, "entities/order.json"))));
        var request = await QueueAsync(scenario, tenant, draft, "author");

        var result = await RunPublisherAsync(scenario, tenant, revisions.Path, folder.Path, request);

        Assert.Equal("failed", await StatusAsync(scenario, tenant, request));
        var diagnostic = Assert.Single(await DiagnosticsAsync(scenario, tenant, request))!;
        Assert.Equal(DiagnosticCodes.AcceptConflict, diagnostic["code"]!.GetValue<string>());
        Assert.Equal(_orderId.ToString("D"), diagnostic["resourceId"]!.GetValue<string>());
        Assert.Equal("entities/order.json", diagnostic["file"]!.GetValue<string>());
        Assert.Contains(version, diagnostic["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} draft failed: {DiagnosticCodes.AcceptConflict} ", StringComparison.Ordinal));
        Assert.Equal(folderBefore, Snapshot(folder.Path));
        Assert.Equal(tip, MainTip(revisions.Path, folder.Path));
        Assert.True(DraftExists(revisions.Path, folder.Path, draft));
    }

    [Fact]
    public async Task An_accept_whose_draft_does_not_compile_fails_with_the_compile_diagnostics_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order());
        using var revisions = new TemporaryFolder();
        var author = NewAuthor(scenario, folder.Path, revisions.Path, "author");
        var draft = await NewDraftAsync(author);
        var broken = Entity(Guid.NewGuid(), "Broken");
        broken["fields"] = new JsonArray(new JsonObject { ["name"] = "number", ["type"] = "nonsense" });
        Written(await author.Drafts.WriteAsync(draft, broken, CancellationToken));
        var folderBefore = Snapshot(folder.Path);
        var tip = MainTip(revisions.Path, folder.Path);
        var request = await QueueAsync(scenario, tenant, draft, "author");

        var result = await RunPublisherAsync(scenario, tenant, revisions.Path, folder.Path, request);

        Assert.Equal("failed", await StatusAsync(scenario, tenant, request));
        var diagnostics = await DiagnosticsAsync(scenario, tenant, request);
        Assert.NotEmpty(diagnostics);
        Assert.DoesNotContain(diagnostics, item => item!["code"]!.GetValue<string>() == DiagnosticCodes.AcceptConflict);
        Assert.Contains(result.Output, line => line.StartsWith($"{tenant} validate failed: ", StringComparison.Ordinal));
        Assert.DoesNotContain($"{tenant} accept ok", result.Output);
        Assert.Equal(folderBefore, Snapshot(folder.Path));
        Assert.Equal(tip, MainTip(revisions.Path, folder.Path));
        Assert.True(DraftExists(revisions.Path, folder.Path, draft));
    }

    [Fact]
    public async Task Two_queued_accepts_of_one_application_run_one_after_the_other()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order());
        using var revisions = new TemporaryFolder();
        var author = NewAuthor(scenario, folder.Path, revisions.Path, "author");
        var first = await NewDraftAsync(author);
        var second = await NewDraftAsync(author);
        Written(await author.Drafts.WriteAsync(first, Entity(Guid.NewGuid(), "Invoice"), CancellationToken));
        Written(await author.Drafts.WriteAsync(second, Entity(Guid.NewGuid(), "Payment"), CancellationToken));
        var revisionCount = author.Revisions.List().Count;
        var firstRequest = await QueueAsync(scenario, tenant, first, "author");
        var secondRequest = await QueueAsync(scenario, tenant, second, "author");

        var result = await RunPublisherAsync(scenario, tenant, revisions.Path, folder.Path, firstRequest, secondRequest);

        Assert.Equal(0, result.Exit);
        Assert.Equal("succeeded", await StatusAsync(scenario, tenant, firstRequest));
        Assert.Equal("succeeded", await StatusAsync(scenario, tenant, secondRequest));
        var finished = (DateTime)(await ScalarAsync(scenario, tenant, $"SELECT finished_at FROM axis.publish_requests WHERE id = '{firstRequest}'"))!;
        var started = (DateTime)(await ScalarAsync(scenario, tenant, $"SELECT started_at FROM axis.publish_requests WHERE id = '{secondRequest}'"))!;
        Assert.True(started >= finished);
        var list = author.Revisions.List();
        Assert.Equal(revisionCount + 2, list.Count);
        Assert.Equal([secondRequest.ToString("D"), firstRequest.ToString("D")], list.Take(2).Select(revision => revision.Request));
        Assert.True(File.Exists(Path.Combine(folder.Path, "entities/invoice.json")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "entities/payment.json")));
    }

    [Fact]
    public async Task An_accept_whose_requester_does_not_own_the_draft_fails_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        using var folder = Application().With("entities/order.json", Order());
        using var revisions = new TemporaryFolder();
        var author = NewAuthor(scenario, folder.Path, revisions.Path, "author");
        var draft = await NewDraftAsync(author);
        Written(await author.Drafts.WriteAsync(draft, Entity(Guid.NewGuid(), "Invoice"), CancellationToken));
        var folderBefore = Snapshot(folder.Path);
        var tip = MainTip(revisions.Path, folder.Path);
        var request = await QueueAsync(scenario, tenant, draft, "other");

        var result = await RunPublisherAsync(scenario, tenant, revisions.Path, folder.Path, request);

        Assert.Equal("failed", await StatusAsync(scenario, tenant, request));
        Assert.Contains($"{tenant} draft failed: The draft is not found.", result.Output);
        Assert.Equal(folderBefore, Snapshot(folder.Path));
        Assert.Equal(tip, MainTip(revisions.Path, folder.Path));
        Assert.True(DraftExists(revisions.Path, folder.Path, draft));
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        return tenant;
    }

    private static Author NewAuthor(AdminScenario scenario, string folder, string revisionsPath, string userId)
    {
        var writeLock = new PlatformStudioWriteLock(NpgsqlDataSource.Create(scenario.Platform), NullLogger<PlatformStudioWriteLock>.Instance);
        return new Author(
            StudioDrafts.For(revisionsPath, folder, userId, "Author User", writeLock)!,
            StudioRevisions.For(revisionsPath, folder, userId, "Author User", NullLogger<StudioRevisions>.Instance)!);
    }

    private static async Task<string> NewDraftAsync(Author author)
    {
        var outcome = await author.Drafts.CreateAsync(author.Revisions, CancellationToken);
        Assert.Null(outcome.Refusal);
        return outcome.Value!.Id;
    }

    private static StudioFile Written(StudioDraftOutcome<StudioFile> outcome)
    {
        Assert.Null(outcome.Refusal);
        return outcome.Value!;
    }

    // Stores the request over the runtime role, as the server does.
    private async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string draft, string requestedBy)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, draft_id, folder, client, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, 'accept', 'queued', @draft, @folder, 'agent', @by, 'Requester', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", _applicationId);
        command.Parameters.AddWithValue("draft", Guid.Parse(draft));
        command.Parameters.AddWithValue("folder", _name);
        command.Parameters.AddWithValue("by", requestedBy);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    // Runs the publisher with the Studio folder until every request is final, then stops it and returns its result.
    private static async Task<AdminResult> RunPublisherAsync(AdminScenario scenario, string tenant, string revisionsPath, string folder, params Guid[] requests)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Studio:Folders:0"] = folder,
            ["Studio:RevisionsPath"] = revisionsPath,
            ["ConnectionStrings:Platform"] = scenario.Platform,
        };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!run.IsCompleted && DateTime.UtcNow < deadline)
            {
                var final = true;
                foreach (var request in requests)
                {
                    final &= await StatusAsync(scenario, tenant, request) is "succeeded" or "failed";
                }

                if (final)
                {
                    break;
                }

                await Task.Delay(200, CancellationToken);
            }
        }
        finally
        {
            await stop.CancelAsync();
        }

        return await run;
    }

    private static async Task<string> StatusAsync(AdminScenario scenario, string tenant, Guid request) =>
        (string)(await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'"))!;

    private static async Task<JsonArray> DiagnosticsAsync(AdminScenario scenario, string tenant, Guid request) =>
        JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT diagnostics::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);

    private static string MainTip(string revisionsPath, string folder)
    {
        using var repository = new Repository(StudioRevisions.RepositoryPath(revisionsPath, folder)!);
        return repository.Refs["refs/heads/main"].TargetIdentifier;
    }

    private static bool DraftExists(string revisionsPath, string folder, string draft)
    {
        using var repository = new Repository(StudioRevisions.RepositoryPath(revisionsPath, folder)!);
        return repository.Refs["refs/heads/drafts/" + draft] is not null;
    }

    // The path and SHA-256 of every file in the folder.
    private static List<(string File, string Version)> Snapshot(string folder) =>
        [.. StudioFolder.Files(folder).Select(file => (file.File, Convert.ToHexStringLower(SHA256.HashData(file.Bytes))))];

    private TemporaryFolder Application() =>
        new TemporaryFolder().With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""");

    private string Order(string? extra = null) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 }{{(extra is null ? "" : $$""", { "name": "{{extra}}", "type": "text" }""")}} ] }
        """;

    private static JsonObject Entity(Guid id, string name) =>
        (JsonObject)JsonNode.Parse(
            $$"""
            { "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1,
              "fields": [ { "name": "number", "type": "text", "required": true, "maxLength": 20 } ] }
            """)!;

    private sealed record Author(StudioDrafts Drafts, StudioRevisions Revisions);
}
