using System.Security.Cryptography;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Studio;
using Axis.Tenancy.Platform;
using LibGit2Sharp;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// In a cell, <c>tenant create</c> gives each tenant a backup key of its own, wrapped by the cell's key in
/// <c>platform.tenant_backup_keys</c>. <c>tenant backup</c> and <c>tenant restore</c> use it in place of <c>--backup-key-file</c>, and an
/// archive does not open with another tenant's key. Outside a cell nothing changes.
/// </summary>
public sealed class AdminCellBackupKeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        File.Delete(_cellKeyFile);
    }

    [Fact]
    public async Task A_tenant_created_in_a_cell_has_a_wrapped_key_and_backs_up_and_restores_into_a_new_tenant_with_its_own_key_without_a_key_file()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        var cellKey = WriteCellKey();
        var cell = CellSettings();
        var source = scenario.NewTenantId();
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(source, $"{source}.example.test"), extra: cell);
        Assert.Equal(0, created.Exit);

        var sourceKey = await UnwrapAsync(scenario, source, cellKey);
        var stored = await StoredKeyAsync(scenario, source);
        Assert.NotEqual(sourceKey, stored);
        Assert.DoesNotContain(Convert.ToBase64String(sourceKey), Convert.ToBase64String(stored), StringComparison.Ordinal);
        Assert.Equal(32, sourceKey.Length);

        // Another tenant id, or the installation key, does not unwrap it.
        Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.FromKey(cellKey).Decrypt("other", TenantSecretFields.BackupKey, stored));
        Assert.Throws<InvalidOperationException>(() => AdminScenario.Cipher.Decrypt(source, TenantSecretFields.BackupKey, stored));

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var backup = await scenario.RunAsync(["tenant", "backup", source, "--out", file], path: tools.PathSetting, extra: cell);
        Assert.Equal(0, backup.Exit);
        Assert.Empty(backup.Error);

        // The archive opens with the tenant's key.
        await using (var archive = File.OpenRead(file))
        await using (var plain = BackupArchive.Decrypt(archive, sourceKey))
        {
            await plain.CopyToAsync(Stream.Null, CancellationToken);
        }

        var restored = scenario.NewTenantId();
        var restore = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test", "--from", source],
            path: tools.PathSetting,
            extra: cell);

        Assert.Equal(0, restore.Exit);
        Assert.Empty(restore.Error);
        var restoredKey = await UnwrapAsync(scenario, restored, cellKey);
        Assert.NotEqual(sourceKey, restoredKey);
        Assert.Equal(sourceKey, await UnwrapAsync(scenario, source, cellKey));

        await AssertNoKeysAsync(scenario, cellKey, [sourceKey, restoredKey], created, backup, restore);
    }

    [Fact]
    public async Task In_a_cell_the_Studio_folder_of_a_tenant_is_backed_up_and_restored_under_the_new_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        WriteCellKey();
        using var studio = new TemporaryFolder();
        var cell = new Dictionary<string, string?>(CellSettings()) { ["Studio:ApplicationsPath"] = studio.Path };
        var source = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(source, $"{source}.example.test"), extra: cell)).Exit);
        var files = new Dictionary<string, byte[]>
        {
            ["app-a/application.json"] = "{\"name\":\"a\"}"u8.ToArray(),
            ["app-a/pages/home.json"] = RandomNumberGenerator.GetBytes(300_000),
        };
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(studio.Path, source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content, CancellationToken);
        }

        // A link is not followed.
        var outside = Path.Combine(studio.Path, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside", CancellationToken);
        File.CreateSymbolicLink(Path.Combine(studio.Path, source, "link.txt"), outside);

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var backup = await scenario.RunAsync(["tenant", "backup", source, "--out", file], path: tools.PathSetting, extra: cell);
        Assert.Equal(0, backup.Exit);

        var restored = scenario.NewTenantId();
        var restore = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test", "--from", source],
            path: tools.PathSetting,
            extra: cell);

        Assert.Equal(0, restore.Exit);
        Assert.Empty(restore.Error);
        string[] steps = ["check", "entry", "roles", "database", "restore", "grants", "grants", "work", "sessions", "role-check", "studio", "version"];
        Assert.Equal(steps.Select(step => $"{restored} {step} ok"), restore.Output);
        var target = Path.Combine(studio.Path, restored);
        var written = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(target, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), written);
        foreach (var (relative, content) in files)
        {
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(target, relative), CancellationToken));
        }

        // The source folder is as it was.
        Assert.Equal(files["app-a/application.json"], await File.ReadAllBytesAsync(Path.Combine(studio.Path, source, "app-a/application.json"), CancellationToken));
    }

    [Fact]
    public async Task In_a_cell_the_revisions_of_a_tenant_are_backed_up_and_restored_with_their_history()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        WriteCellKey();
        using var studio = new TemporaryFolder();
        var cell = new Dictionary<string, string?>(CellSettings()) { ["Studio:ApplicationsPath"] = studio.Path };
        var source = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(source, $"{source}.example.test"), extra: cell)).Exit);
        var sourceFolder = Path.Combine(studio.Path, source);
        var commits = await TenantRevisions.BuildAsync(sourceFolder, "app-a", CancellationToken);
        Assert.Equal(2, commits.Count);

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var backup = await scenario.RunAsync(["tenant", "backup", source, "--out", file], path: tools.PathSetting, extra: cell);
        Assert.Equal(0, backup.Exit);

        var restored = scenario.NewTenantId();
        var restore = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test", "--from", source],
            path: tools.PathSetting,
            extra: cell);

        Assert.Equal(0, restore.Exit);
        Assert.Empty(restore.Error);
        Assert.Contains($"{restored} studio ok", restore.Output);

        // The restored history is the one Studio continues: the path rule is the server's.
        var options = new StudioOptions([], null, null, ApplicationsPath: studio.Path, PerTenant: true).ForTenant(restored);
        var repository = Path.Combine(options.RevisionsPath!, "app-a.git");
        Assert.True(Repository.IsValid(repository));
        Assert.Equal(commits, TenantRevisions.CommitShas(repository));

        var restoredFolder = Path.Combine(studio.Path, restored);
        await File.WriteAllTextAsync(Path.Combine(restoredFolder, "app-a", "application.json"), "{\"name\":\"app-a\",\"version\":3}", CancellationToken);
        TenantRevisions.For(restoredFolder, "app-a").RecordOutside();

        using (var git = new Repository(repository))
        {
            var tip = TenantRevisions.Commits(git)[^1];
            Assert.Equal("Outside change", tip.MessageShort);
            Assert.Equal(commits[^1], Assert.Single(tip.Parents).Sha);
            Assert.Equal(3, TenantRevisions.Commits(git).Count);
        }

        // The source history is as it was.
        Assert.Equal(commits, TenantRevisions.CommitShas(Path.Combine(sourceFolder, ".revisions", "app-a.git")));
    }

    [Fact]
    public async Task In_a_cell_a_tenant_without_a_Studio_folder_backs_up_and_restores_with_no_studio_step()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        WriteCellKey();
        using var studio = new TemporaryFolder();
        var cell = new Dictionary<string, string?>(CellSettings()) { ["Studio:ApplicationsPath"] = studio.Path };
        var source = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(source, $"{source}.example.test"), extra: cell)).Exit);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", source, "--out", file], path: tools.PathSetting, extra: cell)).Exit);
        var restored = scenario.NewTenantId();

        var restore = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test", "--from", source],
            path: tools.PathSetting,
            extra: cell);

        Assert.Equal(0, restore.Exit);
        Assert.Empty(restore.Error);
        string[] steps = ["check", "entry", "roles", "database", "restore", "grants", "grants", "work", "sessions", "role-check", "version"];
        Assert.Equal(steps.Select(step => $"{restored} {step} ok"), restore.Output);
        Assert.False(Directory.Exists(Path.Combine(studio.Path, restored)));
    }

    [Fact]
    public async Task An_archive_of_one_tenant_does_not_open_with_the_key_of_another_and_the_restore_creates_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        var cellKey = WriteCellKey();
        var cell = CellSettings();
        var a = scenario.NewTenantId();
        var b = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(a, $"{a}.example.test"), extra: cell)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(b, $"{b}.example.test"), extra: cell)).Exit);
        Assert.NotEqual(await UnwrapAsync(scenario, a, cellKey), await UnwrapAsync(scenario, b, cellKey));
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "a.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", a, "--out", file], path: tools.PathSetting, extra: cell)).Exit);
        var id = scenario.NewTenantId();
        var entries = await scenario.CountEntriesAsync();

        var result = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", id, "--host", $"{id}.example.test", "--from", b],
            path: tools.PathSetting,
            extra: cell);

        Assert.Equal(2, result.Exit);
        Assert.Equal([$"{id} check failed: the backup key does not open the archive"], result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(entries, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0, await scenario.CountRolesAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task In_a_cell_backup_and_restore_refuse_a_key_file_a_missing_cell_key_and_a_tenant_without_a_key()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        var cellKey = WriteCellKey();
        var cell = CellSettings();
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), extra: cell)).Exit);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", id, "--out", file], path: tools.PathSetting, extra: cell)).Exit);
        var newId = scenario.NewTenantId();
        var entries = await scenario.CountEntriesAsync();
        var out2 = Path.Combine(folder.Path, "second.tar");
        string[] restoreArgs = ["tenant", "restore", file, "--tenant", newId, "--host", $"{newId}.example.test"];

        var backupWithFile = await scenario.RunAsync(["tenant", "backup", id, "--out", out2, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting, extra: cell);
        var restoreWithFile = await scenario.RunAsync([.. restoreArgs, "--from", id, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting, extra: cell);
        var restoreWithFileOnly = await scenario.RunAsync([.. restoreArgs, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting, extra: cell);
        var restoreWithoutFrom = await scenario.RunAsync(restoreArgs, path: tools.PathSetting, extra: cell);

        Assert.Equal(2, backupWithFile.Exit);
        Assert.Equal(["In a cell, tenant backup takes the tenant's backup key from the platform, so --backup-key-file is refused."], backupWithFile.Error);
        foreach (var refused in new[] { restoreWithFile, restoreWithFileOnly })
        {
            Assert.Equal(2, refused.Exit);
            Assert.Single(refused.Error);
            Assert.Contains("--backup-key-file is refused", refused.Error[0], StringComparison.Ordinal);
        }

        Assert.Equal(2, restoreWithoutFrom.Exit);
        Assert.StartsWith("Usage:", restoreWithoutFrom.Error[0], StringComparison.Ordinal);
        Assert.False(File.Exists(out2));

        // A missing, blank or unreadable cell key stops every command that needs it before any tenant work.
        var unreadable = Path.Combine(folder.Path, "missing.key");
        var malformed = Path.Combine(folder.Path, "malformed.key");
        await File.WriteAllTextAsync(malformed, "abc", CancellationToken);
        foreach (var (setting, message) in new (string? Setting, string Message)[]
        {
            (null, "The setting Cell:BackupKeyFile is required."),
            ("", "The setting Cell:BackupKeyFile is required."),
            (unreadable, "The Cell:BackupKeyFile file cannot be read."),
            (malformed, "The Cell:BackupKeyFile file does not hold a key of 32 bytes in base64."),
        })
        {
            var settings = new Dictionary<string, string?>(cell) { ["Cell:BackupKeyFile"] = setting };
            var create = scenario.NewTenantId();
            var results = new[]
            {
                await scenario.RunAsync(AdminScenario.CreateArgs(create, $"{create}.example.test"), extra: settings),
                await scenario.RunAsync(["tenant", "backup", id, "--out", out2], path: tools.PathSetting, extra: settings),
                await scenario.RunAsync([.. restoreArgs, "--from", id], path: tools.PathSetting, extra: settings),
            };
            foreach (var result in results)
            {
                Assert.Equal(2, result.Exit);
                Assert.Equal([message], result.Error);
                Assert.Empty(result.Output);
            }

            Assert.False(await scenario.DatabaseExistsAsync(create));
        }

        Assert.False(File.Exists(out2));
        Assert.Equal(entries, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(newId));

        // A tenant without a key, such as one made before cells, is refused, and an unknown tenant keeps its own message.
        await using (var context = NewContext(scenario))
        {
            await context.TenantBackupKeys.Where(key => key.TenantId == id).ExecuteDeleteAsync(CancellationToken);
        }

        var noKey = await scenario.RunAsync(["tenant", "backup", id, "--out", out2], path: tools.PathSetting, extra: cell);
        var unknown = await scenario.RunAsync(["tenant", "backup", "t-nobody", "--out", out2], path: tools.PathSetting, extra: cell);
        var noFromKey = await scenario.RunAsync([.. restoreArgs, "--from", id], path: tools.PathSetting, extra: cell);
        foreach (var (result, message) in new[]
        {
            (noKey, "The tenant has no backup key."),
            (unknown, "The tenant does not exist."),
            (noFromKey, "The tenant named by --from has no backup key."),
        })
        {
            Assert.Equal(2, result.Exit);
            Assert.Equal([message], result.Error);
        }

        Assert.False(await scenario.DatabaseExistsAsync(newId));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Outside_a_cell_backup_and_restore_use_the_key_file_write_no_backup_key_row_and_refuse_from()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        var source = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(source, $"{source}.example.test")).Exit);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        var backup = await scenario.RunAsync(["tenant", "backup", source, "--out", file, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting);
        var withoutKey = await scenario.RunAsync(["tenant", "backup", source, "--out", Path.Combine(folder.Path, "none.tar")], path: tools.PathSetting);
        var restored = scenario.NewTenantId();
        string[] restoreArgs = ["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test"];
        var withFrom = await scenario.RunAsync([.. restoreArgs, "--from", source, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting);
        var withFromOnly = await scenario.RunAsync([.. restoreArgs, "--from", source], path: tools.PathSetting);
        Assert.False(await scenario.DatabaseExistsAsync(restored));
        var restore = await scenario.RunAsync([.. restoreArgs, .. AdminScenario.BackupKeyArgs], path: tools.PathSetting);

        Assert.Equal(0, backup.Exit);
        Assert.Equal(2, withoutKey.Exit);
        Assert.StartsWith("Usage:", withoutKey.Error[0], StringComparison.Ordinal);
        foreach (var refused in new[] { withFrom, withFromOnly })
        {
            Assert.Equal(2, refused.Exit);
            Assert.StartsWith("Usage:", refused.Error[0], StringComparison.Ordinal);
            Assert.Empty(refused.Output);
        }

        Assert.Equal(0, restore.Exit);
        await using var context = NewContext(scenario);
        Assert.Equal(0, await context.TenantBackupKeys.CountAsync(CancellationToken));
        await scenario.AssertNoSecretsAsync();
    }

    private Dictionary<string, string?> CellSettings() => new()
    {
        ["Tenancy:Source"] = "directory",
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:BackupKeyFile"] = _cellKeyFile,
    };

    private static PlatformDbContext NewContext(AdminScenario scenario) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);

    /// <summary>Writes a new random cell key to a file and returns the key. The file holds the base64 text.</summary>
    private byte[] WriteCellKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_cellKeyFile, Convert.ToBase64String(key) + "\n");
        return key;
    }

    private static async Task<byte[]> StoredKeyAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario);
        return await context.TenantBackupKeys.AsNoTracking().Where(row => row.TenantId == id).Select(row => row.Key).SingleAsync(CancellationToken);
    }

    /// <summary>The tenant's backup key, unwrapped with the cell's key.</summary>
    private static async Task<byte[]> UnwrapAsync(AdminScenario scenario, string id, byte[] cellKey) =>
        Convert.FromBase64String(
            TenantSecretCipher.FromKey(cellKey).Decrypt(id, TenantSecretFields.BackupKey, await StoredKeyAsync(scenario, id)).Reveal());

    /// <summary>Checks that no output held a tenant key or the cell's key, in base64 or in hex, and that no secret of the scenario leaked.</summary>
    private static async Task AssertNoKeysAsync(AdminScenario scenario, byte[] cellKey, byte[][] tenantKeys, params AdminResult[] results)
    {
        var text = string.Join('\n', results.SelectMany(result => result.Output.Concat(result.Error)));
        foreach (var key in tenantKeys.Append(cellKey))
        {
            Assert.DoesNotContain(Convert.ToBase64String(key), text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexString(key), text, StringComparison.OrdinalIgnoreCase);
        }

        await scenario.AssertNoSecretsAsync();
    }
}
