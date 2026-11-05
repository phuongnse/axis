using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Admin;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>export</c> job writes the records of every application of a tenant into a ZIP of JSON Lines and CSV files, with the sensitive
/// values in clear, and stores it with server-side encryption at <c>exports/&lt;tenant&gt;/&lt;job id&gt;.zip</c>. A tenant that is still
/// provisioning is refused and nothing is uploaded. In a cell the job holds a <c>concurrentImports</c> lease of the tenant until the ZIP is
/// stored, and waits without using a claim while none is free. The server of the fixture activates the purchase request and leave request samples.
/// </summary>
public sealed class TenantExportJobTests(AdminBackupFixture fixture, MinioFixture minio) : IClassFixture<AdminBackupFixture>, IClassFixture<MinioFixture>, IAsyncDisposable
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private readonly NpgsqlDataSource _leaseSource = NpgsqlDataSource.Create(fixture.Scenario.Platform);

    public ValueTask DisposeAsync() => _leaseSource.DisposeAsync();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_zip_of_a_tenant_with_two_applications_holds_the_manifest_and_the_jsonl_and_csv_of_each_entity_with_child_rows_and_sensitive_values_in_clear_and_is_stored_encrypted()
    {
        using var client = fixture.CreateClient();
        var request = await AdminBackupFixture.CreateRequestAsync(client);
        var reason = $"Flu {Guid.NewGuid():N}";
        var leave = await CreateLeaveRequestAsync(client, reason);
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);

        var (job, output) = await RunExportAsync(fixture.TenantId, bucket);

        Assert.Contains($"{fixture.TenantId} export ok", output);
        Assert.Equal("succeeded", await StateAsync(job));
        var key = $"exports/{fixture.TenantId}/{job:D}.zip";
        Assert.Equal([key], await KeysAsync(s3, bucket));
        var metadata = await s3.GetObjectMetadataAsync(bucket, key, CancellationToken);
        Assert.Equal(ServerSideEncryptionMethod.AES256, metadata.ServerSideEncryptionMethod);

        var files = await ReadZipAsync(s3, bucket, key);
        using var manifest = JsonDocument.Parse(files["export.json"]);
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal(fixture.TenantId, root.GetProperty("tenantId").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", root.GetProperty("createdAt").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("axisVersion").GetString()));
        var applications = root.GetProperty("applications").EnumerateArray().ToList();
        Assert.Equal(["LeaveRequests", "PurchaseRequests"], applications.Select(application => application.GetProperty("name").GetString()));
        Assert.All(applications, application => Assert.True(application.GetProperty("release").GetInt32() > 0));
        var purchase = applications.Single(application => application.GetProperty("name").GetString() == "PurchaseRequests");
        Assert.Equal("PurchaseRequests", purchase.GetProperty("folder").GetString());
        var entities = purchase.GetProperty("entities").EnumerateArray().Select(entity => (Name: entity.GetProperty("name").GetString(), File: entity.GetProperty("file").GetString())).ToList();
        Assert.Contains(("PurchaseRequest", "PurchaseRequest"), entities);
        Assert.DoesNotContain(entities, entity => entity.Name == "LineItem");

        var requestLine = Assert.Single(Lines(files["data/PurchaseRequests/PurchaseRequest.jsonl"]), line => Id(line) == request);
        using var requestJson = JsonDocument.Parse(requestLine);
        Assert.True(requestJson.RootElement.TryGetProperty("version", out _));
        Assert.True(requestJson.RootElement.TryGetProperty("labels", out _));
        var lineItems = requestJson.RootElement.GetProperty("values").GetProperty("lineItems").EnumerateArray().ToList();
        Assert.Equal("Desk", Assert.Single(lineItems).GetProperty("description").GetString());

        var requestCsv = Csv(files["data/PurchaseRequests/PurchaseRequest.csv"]);
        Assert.Equal("id", requestCsv[0].Split(',')[0]);
        Assert.Contains(requestCsv.Skip(1), row => row.StartsWith(request.ToString("D"), StringComparison.Ordinal));
        var lineItemsCsv = Csv(files["data/PurchaseRequests/PurchaseRequest.lineItems.csv"]);
        Assert.Equal("recordId", lineItemsCsv[0].Split(',')[0]);
        Assert.Contains(lineItemsCsv.Skip(1), row => row.StartsWith(request.ToString("D"), StringComparison.Ordinal) && row.Contains("Desk", StringComparison.Ordinal));

        var leaveLine = Assert.Single(Lines(files["data/LeaveRequests/LeaveRequest.jsonl"]), line => Id(line) == leave);
        Assert.Contains(reason, leaveLine, StringComparison.Ordinal);
        Assert.Contains(Csv(files["data/LeaveRequests/LeaveRequest.csv"]), row => row.StartsWith(leave.ToString("D"), StringComparison.Ordinal) && row.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_module_entity_with_a_slash_in_its_name_is_written_with_an_underscore_and_the_manifest_lists_its_original_name()
    {
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);

        var (job, output) = await RunExportAsync(fixture.TenantId, bucket);

        Assert.Contains($"{fixture.TenantId} export ok", output);
        var files = await ReadZipAsync(s3, bucket, $"exports/{fixture.TenantId}/{job:D}.zip");
        Assert.Contains("data/PurchaseRequests/organization_Department.jsonl", files.Keys);
        Assert.Contains("data/PurchaseRequests/organization_Department.csv", files.Keys);
        Assert.DoesNotContain(files.Keys, name => name.Contains("organization/", StringComparison.Ordinal));
        using var manifest = JsonDocument.Parse(files["export.json"]);
        var purchase = manifest.RootElement.GetProperty("applications").EnumerateArray().Single(application => application.GetProperty("name").GetString() == "PurchaseRequests");
        Assert.Contains(
            purchase.GetProperty("entities").EnumerateArray(),
            entity => entity.GetProperty("name").GetString() == "organization/Department" && entity.GetProperty("file").GetString() == "organization_Department");
        Assert.Contains(Lines(files["data/PurchaseRequests/organization_Department.jsonl"]), line => line.Contains("Operations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_export_job_for_a_provisioning_tenant_fails_with_the_provisioning_refusal_and_uploads_nothing()
    {
        var scenario = fixture.Scenario;
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await scenario.SetStatusAsync(id, "provisioning");
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);

        var (job, output) = await RunExportAsync(id, bucket);

        Assert.Equal("failed", await StateAsync(job));
        Assert.Contains($"{id} export failed: {TenantStatusMessage}", output);
        Assert.Empty(await KeysAsync(s3, bucket));
    }

    [Fact]
    public async Task An_export_job_without_the_storage_setting_fails_naming_the_setting()
    {
        var kinds = Kinds(JobSetting<CellBackupStorage>.Missing("The setting Cell:ExportStorage:Bucket is required."));
        var job = await ProvisioningJobs.QueueAsync(fixture.Scenario.Platform, kinds, "export", fixture.TenantId, "operator-1", null, null, CancellationToken);
        using var output = new StringWriter();

        Assert.True(await new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease).RunNextAsync(output, CancellationToken));

        Assert.Equal("failed", await StateAsync(job));
        Assert.Contains("The setting Cell:ExportStorage:Bucket is required.", output.ToString(), StringComparison.Ordinal);
    }

    private const string TenantStatusMessage = "the tenant is still provisioning. Finish tenant create first.";

    private async Task<(Guid Job, string Output)> RunExportAsync(string tenantId, string bucket)
    {
        var storage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
        var kinds = Kinds(JobSetting<CellBackupStorage>.Of(storage));
        var job = await ProvisioningJobs.QueueAsync(fixture.Scenario.Platform, kinds, "export", tenantId, "operator-1", null, null, CancellationToken);
        using var output = new StringWriter();

        Assert.True(await new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease).RunNextAsync(output, CancellationToken));
        return (job, output.ToString());
    }

    [Fact]
    public async Task A_second_export_of_a_tenant_waits_for_the_first_and_then_succeeds()
    {
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);
        var storage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 1;
        var kinds = Kinds(
            JobSetting<CellBackupStorage>.Of(storage),
            CellDefaults(),
            step => step with
            {
                RunAsync = async (job, tenant, input, cancellationToken) =>
                {
                    if (Interlocked.Exchange(ref first, 0) == 1)
                    {
                        started.SetResult();
                        await gate.Task.WaitAsync(cancellationToken);
                    }

                    return await step.RunAsync(job, tenant, input, cancellationToken);
                },
            });
        var jobA = await ProvisioningJobs.QueueAsync(fixture.Scenario.Platform, kinds, "export", fixture.TenantId, "operator-1", null, null, CancellationToken);
        var jobB = await ProvisioningJobs.QueueAsync(fixture.Scenario.Platform, kinds, "export", fixture.TenantId, "operator-1", null, null, CancellationToken);
        using var outputA = new StringWriter();
        using var outputB = new StringWriter();

        var runnerA = new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease).RunNextAsync(outputA, CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.Equal(1, await LeaseCountAsync(fixture.TenantId));
        Assert.False(await new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease).RunNextAsync(outputB, CancellationToken));
        Assert.Equal("queued", await StateAsync(jobB));

        gate.SetResult();
        Assert.True(await runnerA);
        Assert.Equal("succeeded", await StateAsync(jobA));
        Assert.Equal(0, await LeaseCountAsync(fixture.TenantId));

        Assert.True(await new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease).RunNextAsync(outputB, CancellationToken));
        Assert.Equal("succeeded", await StateAsync(jobB));
        Assert.Equal(new[] { $"exports/{fixture.TenantId}/{jobA:D}.zip", $"exports/{fixture.TenantId}/{jobB:D}.zip" }.Order(StringComparer.Ordinal), await KeysAsync(s3, bucket));
    }

    [Fact]
    public async Task An_export_waits_while_an_import_holds_the_only_concurrentImports_lease_and_runs_after_it_is_freed()
    {
        using var s3 = minio.NewClient();
        var bucket = await NewBucketAsync(s3);
        var storage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
        var kinds = Kinds(JobSetting<CellBackupStorage>.Of(storage), CellDefaults(("Quotas:Defaults:concurrentImports", "1")));
        var job = await ProvisioningJobs.QueueAsync(fixture.Scenario.Platform, kinds, "export", fixture.TenantId, "operator-1", null, null, CancellationToken);
        var runner = new ProvisioningJobs(fixture.Scenario.Platform, kinds, _lease, TimeSpan.Zero);
        using var output = new StringWriter();
        await using var source = NpgsqlDataSource.Create(fixture.Scenario.Platform);
        var import = await new TenantLeases(source, NullLogger<TenantLeases>.Instance).TryTakeAsync(fixture.TenantId, QuotaKeys.ConcurrentImports, 1, CancellationToken);
        Assert.NotNull(import);

        for (var round = 1; round <= 4; round++)
        {
            Assert.True(await runner.RunNextAsync(output, CancellationToken));
            Assert.Equal("running", await StateAsync(job));
            Assert.Empty(await KeysAsync(s3, bucket));
        }

        Assert.Equal(4, Regex.Count(output.ToString(), $"{fixture.TenantId} export waiting"));
        Assert.Equal((4, 0), await ClaimsAsync(job));

        await import.DisposeAsync();
        Assert.True(await runner.RunNextAsync(output, CancellationToken));

        Assert.Equal("succeeded", await StateAsync(job));
        Assert.Equal([$"exports/{fixture.TenantId}/{job:D}.zip"], await KeysAsync(s3, bucket));
        Assert.Contains($"{fixture.TenantId} export ok", output.ToString(), StringComparison.Ordinal);
    }

    private static QuotaDefaults CellDefaults(params (string Setting, string Value)[] settings) =>
        QuotaDefaults.Load(
            new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(setting => setting.Setting, setting => (string?)setting.Value)).Build(),
            new CellOptions { Id = "vn-1" });

    private Dictionary<string, IReadOnlyList<JobStep>> Kinds(JobSetting<CellBackupStorage> storage, QuotaDefaults? quotas = null, Func<JobStep, JobStep>? wrap = null)
    {
        var platform = fixture.Scenario.Platform;
        var sensitive = new SensitiveValues(new TenantKeys(new DirectoryTenantKeySource(platform, AdminScenario.Cipher)));
        var leases = new TenantLeases(_leaseSource, NullLogger<TenantLeases>.Instance);
        var steps = TenantExport.Steps(new TenantExportSettings(platform, AdminScenario.Cipher, PackageCatalog.Load(SamplePackages.Path).Versions, sensitive, storage, leases, quotas));
        return new Dictionary<string, IReadOnlyList<JobStep>>
        {
            ["export"] = wrap is null ? steps : [.. steps.Select(wrap)],
        };
    }

    private async Task<long> LeaseCountAsync(string tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.Scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM platform.tenant_leases WHERE tenant_id = @tenant AND quota = 'concurrentImports'", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<(int Waits, int Claimable)> ClaimsAsync(Guid job)
    {
        await using var connection = new NpgsqlConnection(fixture.Scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT waits, claims - waits FROM platform.jobs WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", job);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task<string?> StateAsync(Guid job)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.Scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand($"SELECT state FROM platform.jobs WHERE id = '{job}'", connection);
        return (string?)await command.ExecuteScalarAsync(CancellationToken);
    }

    private static async Task<Guid> CreateLeaveRequestAsync(HttpClient client, string reason)
    {
        using var departments = Request("/api/apps/LeaveRequests/entities/organization/Department/records?pageSize=100", AdminBackupFixture.Host);
        using var list = await client.SendAsync(departments, CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, list.StatusCode);
        using var found = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
        var department = found.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("values").GetProperty("name").GetString() == "Operations")
            .GetProperty("id").GetGuid();

        var body = JsonSerializer.Serialize(new
        {
            values = new
            {
                title = $"Sick leave {Guid.NewGuid():N}",
                department,
                leaveType = "sick",
                firstDay = "2026-11-02",
                lastDay = "2026-11-03",
                reason,
                status = "draft",
            },
        });
        using var create = Request(HttpMethod.Post, "/api/apps/LeaveRequests/entities/LeaveRequest/records", AdminBackupFixture.Host, body);
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> NewBucketAsync(IAmazonS3 s3)
    {
        var bucket = $"exports-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, CancellationToken);
        return bucket;
    }

    private static async Task<List<string>> KeysAsync(IAmazonS3 s3, string bucket)
    {
        var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, CancellationToken);
        return [.. (response.S3Objects ?? []).Select(item => item.Key).Order(StringComparer.Ordinal)];
    }

    private static async Task<Dictionary<string, string>> ReadZipAsync(IAmazonS3 s3, string bucket, string key)
    {
        using var response = await s3.GetObjectAsync(bucket, key, CancellationToken);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, CancellationToken);
        memory.Position = 0;
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            await using var stream = await entry.OpenAsync(CancellationToken);
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            files.Add(entry.FullName, await reader.ReadToEndAsync(CancellationToken));
        }

        return files;
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Csv(string text) => text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static Guid Id(string line)
    {
        using var json = JsonDocument.Parse(line);
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
