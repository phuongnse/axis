using System.Net;
using System.Security.Cryptography;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.StudioPublishTests;

namespace Axis.Integration.Tests;

/// <summary>
/// The storage check of the Studio write routes, see <c>docs/reference/tenant-quotas.md#refusals</c>. The host is a cell
/// whose storage quota is 1000 bytes, see <see cref="StorageQuotaHost"/>. Tenant <c>a</c> has a latest sample of 2000 bytes
/// in one test and of 10 in the other. The platform tables live in tenant <c>a</c>'s database here, where the server reads them.
/// </summary>
public sealed class StudioStorageQuotaTests : IAsyncLifetime
{
    private const string EntityFile = "entities/purchase-request.json";
    private const string EntityId = "d9ccbf89-5004-4d52-8885-2ee377b7caa5";
    private const string PatchUrl = $"/api/studio/apps/PurchaseRequests/resources/{EntityId}";
    private const string PublishUrl = "/api/studio/apps/PurchaseRequests/publish";
    private const string Title = "This tenant has reached its storage quota.";
    private const string Patch = """[{"op":"replace","path":"/displayField","value":"requester"}]""";
    private const long Limit = 1000;

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Over_the_quota_a_studio_patch_and_a_publish_get_the_quota_403_and_write_nothing()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_storage_quota_over", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]).WithWebHostBuilder(builder => builder.InCell(Limit));
        using var client = factory.CreateClient().SignedInAs("author");
        var path = Path.Combine(folder.Path, EntityFile);
        var before = await File.ReadAllBytesAsync(path, CancellationToken);
        await StorageQuotaHost.SeedUsageAsync(connectionString, "a", 2000, 0, CancellationToken);
        try
        {
            using var patch = await PatchAsync(client, before);
            await AssertQuotaRefusalAsync(patch);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken));

            using var publish = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);
            await AssertQuotaRefusalAsync(publish);
            Assert.Equal(0, await CountRequestsAsync(connectionString));
        }
        finally
        {
            await StorageQuotaHost.ClearUsageAsync(connectionString, CancellationToken);
        }
    }

    [Fact]
    public async Task Under_the_quota_a_studio_patch_answers_200_and_a_publish_answers_202_and_stores_one_request()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_storage_quota_under", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]).WithWebHostBuilder(builder => builder.InCell(Limit));
        using var client = factory.CreateClient().SignedInAs("author");
        var path = Path.Combine(folder.Path, EntityFile);
        var before = await File.ReadAllBytesAsync(path, CancellationToken);
        await StorageQuotaHost.SeedUsageAsync(connectionString, "a", 10, 0, CancellationToken);
        try
        {
            using var patch = await PatchAsync(client, before);
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            Assert.NotEqual(before, await File.ReadAllBytesAsync(path, CancellationToken));

            using var publish = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);
            Assert.Equal(HttpStatusCode.Accepted, publish.StatusCode);
            Assert.Equal(1, await CountRequestsAsync(connectionString));
        }
        finally
        {
            await StorageQuotaHost.ClearUsageAsync(connectionString, CancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, byte[] fileBytes)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Patch, PatchUrl, HostA, Patch, "application/json-patch+json");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Convert.ToHexStringLower(SHA256.HashData(fileBytes))}\"");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task AssertQuotaRefusalAsync(HttpResponseMessage response)
    {
        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        var root = problem.RootElement;
        Assert.Equal(Title, root.GetProperty("title").GetString());
        Assert.Equal("quota-exceeded", root.GetProperty("reason").GetString());
        Assert.Equal("storage", root.GetProperty("quota").GetString());
        Assert.Equal(Limit, root.GetProperty("limit").GetInt64());
        Assert.Equal(2000, root.GetProperty("used").GetInt64());
    }
}
