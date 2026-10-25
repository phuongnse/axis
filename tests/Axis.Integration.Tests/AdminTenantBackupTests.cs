using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantBackupTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private static readonly Guid _purchaseRequestEntity = Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_writes_an_archive_with_the_manifest_of_a_tenant_with_requests_and_a_waiting_instance()
    {
        using var client = fixture.CreateClient();
        await AdminBackupFixture.CreateRequestAsync(client);
        var release = await fixture.ActiveReleaseAsync();
        await fixture.InsertInstanceAsync(release, ProcessStarts.Waiting);
        await fixture.Scenario.SetServiceAudienceAsync(fixture.TenantId, "axis-services");
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.BackupAsync(file);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{fixture.TenantId} dump ok", $"{fixture.TenantId} archive ok"], result.Output);
        Assert.Empty(result.Error);
        Assert.Equal([file], Directory.GetFiles(folder.Path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }

        var archive = ReadArchive(file, folder.Path);
        Assert.Equal(["manifest.json", "tenant.dump"], archive.Names.Order(StringComparer.Ordinal));
        using var manifest = JsonDocument.Parse(archive.Manifest);
        var root = manifest.RootElement;
        var row = await fixture.Scenario.RowAsync(fixture.TenantId);
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal(fixture.TenantId, root.GetProperty("tenantId").GetString());
        Assert.Equal(row.SchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("axisVersion").GetString()));
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("createdAt").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _));

        var package = root.GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "payment-references");
        Assert.True(package.GetProperty("required").GetBoolean());
        Assert.Matches(@"^1\.\d+\.\d+$", package.GetProperty("version").GetString()!);
        var module = root.GetProperty("modules").EnumerateArray().Single();
        Assert.Equal("departments", module.GetProperty("id").GetString());
        Assert.Equal("1.0.0", module.GetProperty("version").GetString());

        var directory = root.GetProperty("directory");
        Assert.Equal("active", directory.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, directory.GetProperty("previewOf").ValueKind);
        Assert.Equal([AdminBackupFixture.Host], directory.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()));
        Assert.Equal(AdminScenario.Authority, directory.GetProperty("oidcAuthority").GetString());
        Assert.Equal("axis", directory.GetProperty("oidcClientId").GetString());
        Assert.Equal("axis-services", directory.GetProperty("serviceAudience").GetString());
        var bindings = directory.GetProperty("bindings").EnumerateArray().ToDictionary(binding => binding.GetProperty("name").GetString()!);
        Assert.Equal("url", bindings["PurchasingSystemUrl"].GetProperty("kind").GetString());
        Assert.Equal("https://purchasing.example.test", bindings["PurchasingSystemUrl"].GetProperty("url").GetString());
        Assert.Equal("secret", bindings["PurchasingSystemToken"].GetProperty("kind").GetString());
        Assert.False(bindings["PurchasingSystemToken"].TryGetProperty("url", out _));
        Assert.DoesNotContain("database", archive.Manifest, StringComparison.OrdinalIgnoreCase);

        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Backup_of_a_preview_tenant_records_its_main_tenant_in_the_manifest()
    {
        var scenario = fixture.Scenario;
        var mainId = scenario.NewTenantId();
        var previewId = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(mainId, $"{mainId}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(previewId, mainId, $"{previewId}.example.test"), withClientSecret: false)).Exit);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await scenario.RunAsync(["tenant", "backup", previewId, "--out", file], path: fixture.Tools.PathSetting);

        Assert.Equal(0, result.Exit);
        using var manifest = JsonDocument.Parse(ReadArchive(file, folder.Path).Manifest);
        Assert.Equal(mainId, manifest.RootElement.GetProperty("directory").GetProperty("previewOf").GetString());
    }

    [Fact]
    public async Task A_package_is_required_only_when_an_active_release_or_a_live_instance_pins_it()
    {
        await fixture.InsertReleaseAsync("pin-live", "1.0.0", ProcessStarts.Waiting);
        await fixture.InsertReleaseAsync("pin-running", "1.0.0", ProcessStarts.Running);
        await fixture.InsertReleaseAsync("pin-ended", "1.0.0", ProcessStarts.Completed);
        await fixture.InsertReleaseAsync("pin-idle", "2.0.0", null);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.BackupAsync(file);

        Assert.Equal(0, result.Exit);
        using var manifest = JsonDocument.Parse(ReadArchive(file, folder.Path).Manifest);
        var packages = manifest.RootElement.GetProperty("packages").EnumerateArray()
            .ToDictionary(p => $"{p.GetProperty("id").GetString()} {p.GetProperty("version").GetString()}", p => p.GetProperty("required").GetBoolean());
        Assert.True(packages["pin-live 1.0.0"]);
        Assert.True(packages["pin-running 1.0.0"]);
        Assert.False(packages["pin-ended 1.0.0"]);
        Assert.False(packages["pin-idle 2.0.0"]);
    }

    [Fact]
    public async Task A_backup_taken_while_records_are_created_restores_every_record_created_before_it_started()
    {
        using var client = fixture.CreateClient();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var created = new ConcurrentQueue<Guid>();
        var writer = Task.Run(
            async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    created.Enqueue(await AdminBackupFixture.CreateRequestAsync(client));
                }
            },
            CancellationToken);

        Guid[] before;
        AdminResult result;
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        try
        {
            await Polling.WaitUntilAsync(() => Task.FromResult(created.Count >= 3 || writer.IsFaulted), "The writer did not create three records.");
            Assert.False(writer.IsFaulted, "Creating records failed.");
            before = [.. created];
            result = await fixture.BackupAsync(file);
        }
        finally
        {
            await stop.CancelAsync();
            await writer;
        }

        Assert.Equal(0, result.Exit);
        var archive = ReadArchive(file, folder.Path);
        var restored = await fixture.NewDatabaseAsync("restored");
        await fixture.Tools.RestoreAsync(archive.DumpFile, CancellationToken, "--no-owner", "--no-privileges", "-d", new NpgsqlConnectionStringBuilder(restored).Database!);
        var ids = string.Join(", ", before.Select(id => $"'{id:D}'"));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_purchaseRequestEntity));
        var count = (long)(await PostgreSqlServer.AdminScalarAsync(restored, $"SELECT count(*) FROM {table} WHERE {EntityNaming.IdColumn} IN ({ids})", CancellationToken))!;
        Assert.Equal(before.Length, count);
    }

    [Fact]
    public async Task The_archive_and_the_output_hold_no_secret_of_the_tenant()
    {
        using var client = fixture.CreateClient();
        await AdminBackupFixture.CreateRequestAsync(client);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.BackupAsync(file);

        Assert.Equal(0, result.Exit);
        var row = await fixture.Scenario.RowAsync(fixture.TenantId);
        string[] secrets =
        [
            AdminScenario.Cipher.Decrypt(row.Id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal(),
            AdminScenario.Cipher.Decrypt(row.Id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal(),
            fixture.Scenario.ClientSecret,
            fixture.BindingSecret,
            .. fixture.Scenario.AdminPasswords,
        ];
        var archive = ReadArchive(file, folder.Path);
        var sql = await fixture.Tools.RestoreAsync(archive.DumpFile, CancellationToken, "--file=-");
        var output = string.Join('\n', result.Output.Concat(result.Error));
        var bytes = await File.ReadAllBytesAsync(file, CancellationToken);
        Assert.NotEmpty(sql);
        foreach (var secret in secrets)
        {
            Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0, "The archive holds a secret.");
            Assert.DoesNotContain(secret, archive.Manifest, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, sql, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        }

        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Backup_of_an_unknown_tenant_exits_2_and_writes_no_file()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.Scenario.RunAsync(["tenant", "backup", "no-such-tenant", "--out", file], path: fixture.Tools.PathSetting);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Single(result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Backup_of_a_provisioning_tenant_exits_2_and_writes_no_file()
    {
        var id = fixture.Scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        await fixture.Scenario.RunAsync(AdminScenario.CreateArgs(id, "half.example.test"), new CancelOnLineWriter(stop), stop);
        Assert.Equal("provisioning", (await fixture.Scenario.RowAsync(id)).Status);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.Scenario.RunAsync(["tenant", "backup", id, "--out", file], path: fixture.Tools.PathSetting);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("provisioning", result.Error[0], StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Backup_with_an_existing_out_file_exits_2_and_leaves_it_as_it_was()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        await File.WriteAllBytesAsync(file, [1, 2, 3], CancellationToken);

        var result = await fixture.BackupAsync(file);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(file, CancellationToken));
        Assert.Equal([file], Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Backup_exits_2_and_writes_nothing_for_a_missing_folder_a_missing_pg_dump_or_a_wrong_command_line()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var scenario = fixture.Scenario;

        var noFolder = await fixture.BackupAsync(Path.Combine(folder.Path, "missing", "backup.tar"));
        var noTool = await scenario.RunAsync(["tenant", "backup", fixture.TenantId, "--out", file], path: folder.Path);
        var noPath = await scenario.RunAsync(["tenant", "backup", fixture.TenantId, "--out", file]);
        var noOut = await scenario.RunAsync(["tenant", "backup", fixture.TenantId], path: fixture.Tools.PathSetting);
        var twice = await scenario.RunAsync(["tenant", "backup", fixture.TenantId, "--out", file, "--out", file], path: fixture.Tools.PathSetting);

        Assert.Equal(2, noFolder.Exit);
        Assert.Equal(2, noTool.Exit);
        Assert.Contains("pg_dump", noTool.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noPath.Exit);
        Assert.Contains("PATH", noPath.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noOut.Exit);
        Assert.StartsWith("Usage:", noOut.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, twice.Exit);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failed_dump_exits_1_leaves_no_file_and_never_prints_what_pg_dump_wrote()
    {
        using var tools = new TemporaryFolder();
        PostgresClientTools.WriteScript(
            Path.Combine(tools.Path, "pg_dump"),
            "#!/bin/sh\necho 'connection to db.internal.example failed' >&2\necho partial\nexit 3");
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var result = await fixture.Scenario.RunAsync(["tenant", "backup", fixture.TenantId, "--out", file], path: tools.Path);

        Assert.Equal(1, result.Exit);
        Assert.Equal([$"{fixture.TenantId} dump failed: pg_dump exited with code 3"], result.Output);
        Assert.Empty(result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
        await fixture.Scenario.AssertNoSecretsAsync();
    }

    private static Archive ReadArchive(string file, string folder)
    {
        var names = new List<string>();
        string? manifest = null;
        var dump = Path.Combine(folder, "extracted.dump");
        using var stream = File.OpenRead(file);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
            if (entry.Name == "manifest.json")
            {
                using var text = new StreamReader(entry.DataStream!, Encoding.UTF8);
                manifest = text.ReadToEnd();
            }
            else
            {
                using var output = File.Create(dump);
                entry.DataStream!.CopyTo(output);
            }
        }

        return new Archive(names, manifest!, dump);
    }

    private sealed record Archive(List<string> Names, string Manifest, string DumpFile);
}

/// <summary>
/// One platform database with one active tenant, <c>t-…</c>, made by <c>tenant create</c> and served by the real server on
/// <see cref="Host"/>. The tenant has a <c>url</c> binding and a <c>secret</c> binding, and the server activates the purchase
/// request and leave request samples in it, so releases pin the payment references package and the departments module.
/// </summary>
public sealed class AdminBackupFixture : IAsyncLifetime
{
    public const string Host = "backup.example.test";

    private const string UrlBinding = "PurchasingSystemUrl";

    private const string SecretBinding = "PurchasingSystemToken";

    private readonly PostgreSqlFixture _database = new();
    private WebApplicationFactory<Program>? _factory;
    private string _schemaConnection = "";

    public string TenantId { get; private set; } = "";

    /// <summary>The random value of the tenant's <c>secret</c> binding.</summary>
    public string BindingSecret { get; } = $"binding-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

    internal AdminScenario Scenario { get; private set; } = null!;

    internal PostgresClientTools Tools { get; private set; } = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        Scenario = await AdminScenario.NewAsync(_database, migrate: true, CancellationToken);
        Tools = await PostgresClientTools.CreateAsync();
        TenantId = Scenario.NewTenantId();
        var create = await Scenario.CreateAsync(TenantId, Host);
        Assert.Equal(0, create.Exit);

        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Scenario.Platform).Options))
        {
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = TenantId, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = TenantId,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(TenantId, TenantSecretFields.Binding(SecretBinding), BindingSecret),
                });
            await context.SaveChangesAsync(CancellationToken);
        }

        var row = await Scenario.RowAsync(TenantId);
        var schema = AdminScenario.Cipher.Decrypt(TenantId, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var runtime = AdminScenario.Cipher.Decrypt(TenantId, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        _schemaConnection = Scenario.TenantConnectionString(TenantId, row.SchemaUser, schema);
        var runtimeConnection = Scenario.TenantConnectionString(TenantId, row.RuntimeUser, runtime);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(Scenario.Platform);
            builder.UseSetting($"Tenants:{TenantId}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{TenantId}:ConnectionString", runtimeConnection);
            builder.UseSetting($"Tenants:{TenantId}:SchemaConnectionString", _schemaConnection);
            builder.UseTestOidc(TenantId);
            builder.UseSetting($"Tenants:{TenantId}:Bindings:{UrlBinding}:Kind", "url");
            builder.UseSetting($"Tenants:{TenantId}:Bindings:{UrlBinding}:Value", "https://purchasing.example.test");
            builder.UseSetting($"Tenants:{TenantId}:Bindings:{SecretBinding}:Kind", "secret");
            builder.UseSetting($"Tenants:{TenantId}:Bindings:{SecretBinding}:Value", BindingSecret);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("ActivateOnStartup:0", Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"));
            builder.UseSetting("ActivateOnStartup:1", Path.Combine(AppContext.BaseDirectory, "Samples", "leave-requests"));
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna Employee", "employee"));
        });

        // Creating a client starts the server, which activates both samples and seeds them.
        _factory.CreateClient().Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Tools?.Dispose();
        if (Scenario is not null)
        {
            await Scenario.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>A new client of the server, signed in as <c>anna</c>. The caller disposes it.</summary>
    public HttpClient CreateClient() => (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient().SignedInAs("anna");

    /// <summary>Creates a new database on the shared server and returns the schema role's connection string to it.</summary>
    public Task<string> NewDatabaseAsync(string prefix) => _database.CreateDatabaseAsync(prefix, CancellationToken);

    /// <summary>Runs <c>tenant backup</c> for the tenant with the wrapped <c>pg_dump</c>.</summary>
    internal Task<AdminResult> BackupAsync(string file) =>
        Scenario.RunAsync(["tenant", "backup", TenantId, "--out", file], path: Tools.PathSetting);

    /// <summary>Runs a statement in the tenant database as the schema role.</summary>
    internal async Task ExecuteAsync(string sql) => await ScalarAsync(sql);

    /// <summary>Creates a draft Operations request through the record API and returns its id, after the response came back.</summary>
    public static async Task<Guid> CreateRequestAsync(HttpClient client)
    {
        using var request = Request(
            HttpMethod.Post,
            "/api/apps/PurchaseRequests/entities/PurchaseRequest/records",
            Host,
            $$"""
            {
              "values": {
                "title": "Request {{Guid.NewGuid():N}}",
                "status": "draft",
                "department": "{{SampleAppFixture.Operations:D}}",
                "lineItems": [{ "description": "Desk", "quantity": 1, "unitPrice": 10 }]
              }
            }
            """);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The id of the active release of the purchase request sample.</summary>
    public async Task<Guid> ActiveReleaseAsync() =>
        (Guid)(await ScalarAsync($"SELECT release_id FROM axis.active_releases WHERE application_id = '{PurchaseRequestsApplication}'"))!;

    /// <summary>Inserts a process instance of the release in the given state, as the tests of pinned releases do.</summary>
    public async Task InsertInstanceAsync(Guid releaseId, string state) =>
        await ScalarAsync(
            $"""
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, ended_at)
            VALUES ('{Guid.NewGuid()}', '{PurchaseRequestsApplication}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{releaseId}', '{state}', 1, 'start',
                {(state is ProcessStarts.Running or ProcessStarts.Waiting ? "NULL" : "now()")})
            """);

    /// <summary>
    /// Stores a release of the purchase request sample that pins one package version, which is not active, with an instance in
    /// <paramref name="state"/> when that is not null. Returns the release id.
    /// </summary>
    public async Task<Guid> InsertReleaseAsync(string packageId, string version, string? state)
    {
        var id = Guid.NewGuid();
        await ScalarAsync(
            $"""
            INSERT INTO axis.releases (id, application_id, content_hash, created_at)
            VALUES ('{id}', '{PurchaseRequestsApplication}', '{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}', now());
            INSERT INTO axis.release_packages (release_id, package_id, version) VALUES ('{id}', '{packageId}', '{version}')
            """);
        if (state is not null)
        {
            await InsertInstanceAsync(id, state);
        }

        return id;
    }

    private static Guid PurchaseRequestsApplication { get; } = Guid.Parse("219102bc-864f-42ed-874a-c19eaee0d1ba");

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_schemaConnection);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : value;
    }
}
