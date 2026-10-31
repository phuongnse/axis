using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant backup</c> encrypts the whole archive with the operator's backup key and carries the tenant's keys, and <c>tenant restore</c>
/// needs the same key. An archive that is not encrypted, or that the key does not open, is refused before anything changes, and a chunk that
/// is changed later fails the <c>restore</c> step.
/// </summary>
public sealed class AdminEncryptedBackupTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const int ManyRequests = 100_000;
    private const string Marker = "PR-ENC";

    private static readonly Guid _purchaseRequestEntity = Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tenant_backed_up_and_restored_with_the_key_has_the_same_record_and_the_same_numbered_keys()
    {
        var scenario = fixture.Scenario;
        using var client = fixture.CreateClient();
        var requestId = await AdminBackupFixture.CreateRequestAsync(client);
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_purchaseRequestEntity));
        var select = $"SELECT f_title FROM {table} WHERE {EntityNaming.IdColumn} = '{requestId:D}'";
        var title = (string)(await PostgreSqlServer.AdminScalarAsync(TenantDatabase(fixture.TenantId), select, CancellationToken))!;
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var newId = scenario.NewTenantId();

        var restore = await scenario.RunAsync(AdminTenantRestoreTests.RestoreArgs(file, newId, $"{newId}.example.test"), path: fixture.Tools.PathSetting);

        Assert.Equal(0, restore.Exit);
        var original = await KeysAsync(fixture.TenantId);
        var restored = await KeysAsync(newId);
        Assert.Equal(2, original.Count);
        Assert.Equal(original, restored);

        // The restored tenant's runtime role reads the record of the original.
        var row = await scenario.RowAsync(newId);
        var runtime = AdminScenario.Cipher.Decrypt(newId, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(newId, row.RuntimeUser, runtime));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(select, connection);
        Assert.Equal(title, (string)(await command.ExecuteScalarAsync(CancellationToken))!);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Restore_without_the_key_or_with_a_wrong_key_or_an_archive_that_is_not_encrypted_or_is_cut_exits_2_and_creates_nothing()
    {
        var scenario = fixture.Scenario;
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);

        var plain = Path.Combine(folder.Path, "plain.tar");
        await using (var source = File.OpenRead(file))
        await using (var decrypted = BackupArchive.Decrypt(source, AdminScenario.BackupKeyBytes))
        await using (var target = File.Create(plain))
        {
            await decrypted.CopyToAsync(target, CancellationToken);
        }

        var cut = Path.Combine(folder.Path, "cut.tar");
        await File.WriteAllBytesAsync(cut, (await File.ReadAllBytesAsync(file, CancellationToken))[..1000], CancellationToken);
        var malformed = Path.Combine(folder.Path, "malformed.key");
        await File.WriteAllTextAsync(malformed, "abc", CancellationToken);
        var wrong = Path.Combine(folder.Path, "wrong.key");
        await File.WriteAllTextAsync(wrong, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), CancellationToken);
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";
        var entries = await scenario.CountEntriesAsync();

        async Task<AdminResult> RunAsync(string archive, params string[] key) =>
            await scenario.RunAsync(["tenant", "restore", archive, "--tenant", id, "--host", host, .. key], path: fixture.Tools.PathSetting);

        var noOption = await RunAsync(file);
        var missingFile = await RunAsync(file, "--backup-key-file", Path.Combine(folder.Path, "missing.key"));
        var malformedKey = await RunAsync(file, "--backup-key-file", malformed);
        var wrongKey = await RunAsync(file, "--backup-key-file", wrong);
        var notEncrypted = await RunAsync(plain, AdminScenario.BackupKeyArgs);
        var cutArchive = await RunAsync(cut, AdminScenario.BackupKeyArgs);

        Assert.Equal(2, noOption.Exit);
        Assert.StartsWith("Usage:", noOption.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, missingFile.Exit);
        Assert.Equal(["The --backup-key-file file cannot be read."], missingFile.Error);
        Assert.Equal(2, malformedKey.Exit);
        Assert.Equal(["The --backup-key-file file does not hold a key of 32 bytes in base64."], malformedKey.Error);
        foreach (var result in new[] { noOption, missingFile, malformedKey })
        {
            Assert.Empty(result.Output);
        }

        Assert.Equal(2, wrongKey.Exit);
        Assert.Equal([$"{id} check failed: the backup key does not open the archive"], wrongKey.Output);
        Assert.Equal(2, notEncrypted.Exit);
        Assert.Equal([$"{id} check failed: the archive is not an encrypted backup"], notEncrypted.Output);
        Assert.Equal(2, cutArchive.Exit);
        Assert.Single(cutArchive.Output);
        Assert.StartsWith($"{id} check failed: ", cutArchive.Output[0], StringComparison.Ordinal);
        Assert.Empty(wrongKey.Error.Concat(notEncrypted.Error).Concat(cutArchive.Error));

        Assert.Equal(entries, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0, await scenario.CountRolesAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_archive_bytes_hold_no_dump_header_manifest_name_tenant_id_tenant_key_or_backup_key()
    {
        using var client = fixture.CreateClient();
        await AdminBackupFixture.CreateRequestAsync(client);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);

        var bytes = await File.ReadAllBytesAsync(file, CancellationToken);
        var keys = await KeysAsync(fixture.TenantId);
        List<byte[]> needles =
        [
            "PGDMP"u8.ToArray(),
            "manifest.json"u8.ToArray(),
            "tenant.dump"u8.ToArray(),
            Encoding.UTF8.GetBytes(fixture.TenantId),
            Encoding.UTF8.GetBytes(AdminScenario.BackupKey),
            AdminScenario.BackupKeyBytes,
        ];
        foreach (var (_, _, key) in keys)
        {
            needles.Add(Encoding.UTF8.GetBytes(key));
            needles.Add(Convert.FromBase64String(key));
        }

        Assert.All(needles, needle => Assert.True(bytes.AsSpan().IndexOf(needle) < 0, "The archive holds something that must be encrypted."));
        Assert.StartsWith("AXISBAK1", Encoding.ASCII.GetString(bytes, 0, 8), StringComparison.Ordinal);

        // The manifest of the decrypted archive holds the two keys, which is the reason the archive is encrypted.
        await using var stream = File.OpenRead(file);
        await using var decrypted = BackupArchive.Decrypt(stream, AdminScenario.BackupKeyBytes);
        using var reader = new TarReader(decrypted);
        using var manifest = JsonDocument.Parse(reader.GetNextEntry()!.DataStream!);
        var listed = manifest.RootElement.GetProperty("keys").EnumerateArray()
            .Select(key => (key.GetProperty("kind").GetString()!, key.GetProperty("number").GetInt32(), key.GetProperty("key").GetString()!))
            .ToList();
        Assert.Equal(2, listed.Count);
        Assert.Equal(keys, listed);

        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_changed_byte_in_a_middle_chunk_fails_the_restore_step_and_the_rerun_with_the_good_archive_finishes()
    {
        var scenario = fixture.Scenario;
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_purchaseRequestEntity));
        await fixture.ExecuteAsync(
            $"""
            INSERT INTO {table} (id, f_number, f_title, f_department, f_status)
            SELECT gen_random_uuid(), '{Marker}' || n, md5(n::text || random()::text), '{SampleAppFixture.Operations:D}', 'draft'
            FROM generate_series(1, {ManyRequests}) AS n
            """);
        try
        {
            using var folder = new TemporaryFolder();
            var file = Path.Combine(folder.Path, "backup.tar");
            Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
            var bytes = await File.ReadAllBytesAsync(file, CancellationToken);
            const int chunk = BackupArchive.ChunkSize + BackupArchive.TagSize;
            Assert.True(bytes.Length > BackupArchive.HeaderSize + (2 * chunk), $"The archive is {bytes.Length} bytes, which is not three chunks.");
            var changed = Path.Combine(folder.Path, "changed.tar");
            var damaged = (byte[])bytes.Clone();
            damaged[BackupArchive.HeaderSize + chunk + 100] ^= 0xFF;
            await File.WriteAllBytesAsync(changed, damaged, CancellationToken);
            var id = scenario.NewTenantId();
            var host = $"{id}.example.test";

            var failed = await scenario.RunAsync(AdminTenantRestoreTests.RestoreArgs(changed, id, host), path: fixture.Tools.PathSetting);

            Assert.Equal(1, failed.Exit);
            Assert.StartsWith($"{id} check ok", failed.Output[0], StringComparison.Ordinal);
            Assert.StartsWith($"{id} restore failed: the archive does not decrypt: ", failed.Output[^1], StringComparison.Ordinal);
            Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);

            var again = await scenario.RunAsync(AdminTenantRestoreTests.RestoreArgs(file, id, host), path: fixture.Tools.PathSetting);

            Assert.Equal(0, again.Exit);
            Assert.Equal("suspended", (await scenario.RowAsync(id)).Status);
            Assert.Equal(2, (await scenario.KeysAsync(id)).Count);
            var count = (long)(await PostgreSqlServer.AdminScalarAsync(
                TenantDatabase(id), $"SELECT count(*) FROM {table} WHERE f_number LIKE '{Marker}%'", CancellationToken))!;
            Assert.Equal(ManyRequests, count);
            await scenario.AssertNoSecretsAsync();
        }
        finally
        {
            await fixture.ExecuteAsync($"DELETE FROM {table} WHERE f_number LIKE '{Marker}%'");
        }
    }

    private string TenantDatabase(string id) =>
        new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform) { Database = $"axis_{id}" }.ConnectionString;

    // The plain keys of a tenant in the order of kind and number, each decrypted with the tenant's own id.
    private async Task<List<(string Kind, int Number, string Key)>> KeysAsync(string id) =>
        [.. (await fixture.Scenario.KeysAsync(id)).Select(row => (
            row.Kind,
            row.Number,
            AdminScenario.Cipher.Decrypt(
                id,
                row.Kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(row.Number) : TenantSecretFields.LookupKey(row.Number),
                row.Key).Reveal()))];
}
