using System.Net;
using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Studio writes take a PostgreSQL advisory lock in the platform database, keyed by the application folder, so server
/// replicas that share a folder never both accept a write that carries the same version. Each test holds the lock of a
/// folder on its own connection, starts the requests, waits until they queue on the lock, and then releases it.
/// </summary>
public sealed class StudioWriteLockTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string EntityFile = "entities/purchase-request.json";

    private const string EntityId = "d9ccbf89-5004-4d52-8885-2ee377b7caa5";

    private const string DepartmentFile = "entities/department.json";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_patches_with_the_same_version_on_two_instances_give_one_200_and_one_409_and_the_file_holds_only_the_200_change()
    {
        await using var replicas = await Replicas.StartAsync(database);
        var path = Path.Combine(replicas.FolderA, EntityFile);
        var version = Sha256(path);
        var held = await HoldAsync(replicas.Platform, replicas.FolderA);

        var first = PatchAsync(replicas.First, "PurchaseRequests", EntityId, "number", version);
        var second = PatchAsync(replicas.Second, "PurchaseRequests", EntityId, "requester", version);
        await WaitForWaitersAsync(replicas.Platform, 2);
        await held.ReleaseAsync();
        var responses = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal($"\"{Sha256(path)}\"", winner.Headers.ETag?.Tag);
        var loserValue = winner == responses[0] ? "requester" : "number";
        Assert.DoesNotContain($"\"displayField\": \"{loserValue}\"", await File.ReadAllTextAsync(path, CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_deletes_with_the_same_version_on_two_instances_give_one_200_and_one_409_and_the_file_is_gone()
    {
        await using var replicas = await Replicas.StartAsync(database);
        var path = Path.Combine(replicas.FolderA, DepartmentFile);
        var id = IdOf(path);
        var version = Sha256(path);
        var held = await HoldAsync(replicas.Platform, replicas.FolderA);

        var first = DeleteAsync(replicas.First, "PurchaseRequests", id, version);
        var second = DeleteAsync(replicas.Second, "PurchaseRequests", id, version);
        await WaitForWaitersAsync(replicas.Platform, 2);
        await held.ReleaseAsync();
        var responses = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_write_to_another_folder_does_not_wait_for_the_lock_of_a_folder()
    {
        await using var replicas = await Replicas.StartAsync(database);
        var held = await HoldAsync(replicas.Platform, replicas.FolderA);

        var blocked = PatchAsync(replicas.First, "PurchaseRequests", EntityId, "number", Sha256(Path.Combine(replicas.FolderA, EntityFile)));
        await WaitForWaitersAsync(replicas.Platform, 1);
        var other = await PatchAsync(replicas.Second, "PurchaseRequestsB", EntityId, "number", Sha256(Path.Combine(replicas.FolderB, EntityFile)));

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.False(blocked.IsCompleted);

        await held.ReleaseAsync();
        Assert.Equal(HttpStatusCode.OK, (await blocked).StatusCode);
    }

    [Fact]
    public async Task A_write_that_is_refused_leaves_no_lock_held()
    {
        await using var replicas = await Replicas.StartAsync(database);
        var path = Path.Combine(replicas.FolderA, EntityFile);
        var stale = Sha256(path);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(replicas.First, "PurchaseRequests", EntityId, "number", stale)).StatusCode);

        var conflict = await PatchAsync(replicas.Second, "PurchaseRequests", EntityId, "title", stale);
        var invalid = await SendPatchAsync(
            replicas.Second, "PurchaseRequests", EntityId, """[{"op":"replace","path":"/id","value":"x"}]""", Sha256(path));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0L, await AdvisoryLockCountAsync(replicas.Platform, granted: null));
        await using var connection = new NpgsqlConnection(replicas.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", PlatformStudioWriteLock.Key(replicas.FolderA));
        Assert.True((bool)(await command.ExecuteScalarAsync(CancellationToken))!);
        await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        unlock.Parameters.AddWithValue("key", PlatformStudioWriteLock.Key(replicas.FolderA));
        await unlock.ExecuteScalarAsync(CancellationToken);
    }

    // Two requests, as two instances of the same server send them: a replace of one field with its own value.
    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string application, string id, string displayField, string version) =>
        SendPatchAsync(client, application, id, $$"""[{"op":"replace","path":"/displayField","value":"{{displayField}}"}]""", version);

    private static async Task<HttpResponseMessage> SendPatchAsync(HttpClient client, string application, string id, string patch, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/studio/apps/{application}/resources/{id}", UriKind.Relative))
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string application, string id, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/studio/apps/{application}/resources/{id}", UriKind.Relative));
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request, CancellationToken);
    }

    // Takes the folder's lock on a connection of the test and keeps it until it is released.
    private static async Task<HeldLock> HoldAsync(string platform, string folder)
    {
        var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", PlatformStudioWriteLock.Key(folder));
        await command.ExecuteNonQueryAsync(CancellationToken);
        return new HeldLock(connection, transaction);
    }

    // Waits until the requests queue on the lock. The deadline is below the 10 second wait of the lock.
    private static async Task WaitForWaitersAsync(string platform, long expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await AdvisoryLockCountAsync(platform, granted: false) < expected)
        {
            Assert.True(DateTime.UtcNow < deadline, "The requests did not queue on the lock in time.");
            await Task.Delay(50, CancellationToken);
        }
    }

    // The advisory locks of the platform database that are granted or waiting, or only one of the two.
    private static async Task<long> AdvisoryLockCountAsync(string platform, bool? granted)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory'
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
              AND (@granted::boolean IS NULL OR granted = @granted)
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("granted", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = (object?)granted ?? DBNull.Value });
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static string IdOf(string path) =>
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(path))!["id"]!.GetValue<string>();

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class HeldLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        public async Task ReleaseAsync()
        {
            await transaction.CommitAsync(CancellationToken);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    // Two server instances on one platform database. Both serve the same two folders. Folder B has another
    // application name, so the instances tell the folders apart.
    private sealed class Replicas : IAsyncDisposable
    {
        private readonly TemporaryFolder _a;
        private readonly TemporaryFolder _b;
        private readonly WebApplicationFactory<Program> _first;
        private readonly WebApplicationFactory<Program> _second;

        private Replicas(string platform, TemporaryFolder a, TemporaryFolder b, WebApplicationFactory<Program> first, WebApplicationFactory<Program> second)
        {
            Platform = platform;
            _a = a;
            _b = b;
            _first = first;
            _second = second;
            First = first.CreateClient().SignedInAs("hoa");
            Second = second.CreateClient().SignedInAs("hoa");
        }

        public string Platform { get; }

        public string FolderA => _a.Path;

        public string FolderB => _b.Path;

        public HttpClient First { get; }

        public HttpClient Second { get; }

        // The instances start one after the other, because both run the platform migrations.
        public static async Task<Replicas> StartAsync(PostgreSqlFixture database)
        {
            var platform = await database.CreateDatabaseAsync("studio_lock", CancellationToken);
            var a = StudioPublishTests.NewFolder();
            var b = StudioPublishTests.NewFolder();
            var manifest = Path.Combine(b.Path, "application.json");
            await File.WriteAllTextAsync(
                manifest,
                (await File.ReadAllTextAsync(manifest, CancellationToken)).Replace("\"PurchaseRequests\"", "\"PurchaseRequestsB\"", StringComparison.Ordinal),
                CancellationToken);

            var first = CreateFactory(database, platform, a.Path, b.Path);
            _ = first.CreateClient();
            var second = CreateFactory(database, platform, a.Path, b.Path);
            return new Replicas(platform, a, b, first, second);
        }

        public async ValueTask DisposeAsync()
        {
            First.Dispose();
            Second.Dispose();
            await _first.DisposeAsync();
            await _second.DisposeAsync();
            _a.Dispose();
            _b.Dispose();
        }

        private static WebApplicationFactory<Program> CreateFactory(PostgreSqlFixture database, string platform, string folderA, string folderB) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseTestPlatform(platform);
                builder.UseSetting("Tenants:default:Hosts:0", "localhost");
                builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
                builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
                builder.UseSetting("Studio:Folders:0", folderA);
                builder.UseSetting("Studio:Folders:1", folderB);
                builder.UseSetting("Studio:AuthorRole", "author");
                builder.UseTestOidc("default");
                builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"));
            });
    }
}
