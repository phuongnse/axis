using System.Globalization;
using Axis.Administration;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant binding list</c>, <c>set</c> and <c>remove</c> and <c>tenant host add</c>, <c>remove</c> and <c>move</c>, and
/// <c>tenant email set</c> and <c>clear</c>. The rules are in <c>Axis.Administration</c>. This class parses the arguments, reads
/// the settings and prints. A message never holds a secret value, and the usage text never echoes an argument.
/// </summary>
internal static class TenantSettings
{
    private static readonly string[] _header = ["name", "kind", "value"];

    private sealed record Setup(string Connection, TenantSecretCipher? Cipher);

    public static async Task<int> BindingListAsync(AdminRun run)
    {
        if (run.Args.Length != 4 || !IsName(run.Args[3]))
        {
            return Usage(run);
        }

        var (setup, exit) = await PrepareAsync(run, needsCipher: false);
        if (setup is null)
        {
            return exit;
        }

        IReadOnlyList<TenantBindingView>? bindings;
        try
        {
            await using var platform = NewContext(setup.Connection);
            bindings = await TenantBindingCommands.ListAsync(platform, run.Args[3], run.CancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            run.Error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return 1;
        }

        if (bindings is null)
        {
            run.Error.WriteLine("The tenant does not exist.");
            return 2;
        }

        var table = new List<string[]> { _header };
        table.AddRange(bindings.Select(binding => new[] { binding.Name, binding.Kind, Value(binding) }));
        var widths = Enumerable.Range(0, _header.Length).Select(column => table.Max(row => row[column].Length)).ToArray();
        foreach (var row in table)
        {
            run.Output.WriteLine(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }

        return 0;
    }

    public static async Task<int> BindingSetAsync(AdminRun run)
    {
        var args = run.Args;
        var isUrl = args.Length == 7 && args[5] == "--url";
        var isStorage = args.Length == 7 && args[5] == "--storage";
        var isSecret = args.Length == 6 && args[5] == "--secret";
        if (!(isUrl || isStorage || isSecret) || !IsName(args[3]) || !IsName(args[4]))
        {
            return Usage(run);
        }

        var (setup, exit) = await PrepareAsync(run, needsCipher: isSecret);
        if (setup is null)
        {
            return exit;
        }

        var id = args[3];
        TenantSettingResult result;
        try
        {
            await using var platform = NewContext(setup.Connection);
            if (isUrl)
            {
                result = await TenantBindingCommands.SetUrlAsync(platform, run.Command.Name, id, args[4], args[6], run.CancellationToken);
            }
            else if (isStorage)
            {
                result = await TenantBindingCommands.SetStorageAsync(platform, run.Command.Name, id, args[4], args[6], run.CancellationToken);
            }
            else
            {
                // The value is wrapped at once, so that no later line can print it.
                var secret = await ReadSecretAsync(run);
                if (secret is null)
                {
                    run.Error.WriteLine($"The value of the secret binding is required, in the environment variable {AdminCommands.BindingSecretSetting} or on standard input.");
                    return 2;
                }

                result = await TenantBindingCommands.SetSecretAsync(platform, setup.Cipher!, run.Command.Name, id, args[4], secret, run.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return Print(run, id, "binding", result);
    }

    public static async Task<int> BindingRemoveAsync(AdminRun run)
    {
        if (run.Args.Length != 5 || !IsName(run.Args[3]) || !IsName(run.Args[4]))
        {
            return Usage(run);
        }

        return await ChangeAsync(
            run,
            run.Args[3],
            "binding",
            (platform, command) => TenantBindingCommands.RemoveAsync(platform, command, run.Args[3], run.Args[4], run.CancellationToken));
    }

    public static async Task<int> EmailSetAsync(AdminRun run)
    {
        var request = ParseEmailSet(run.Args);
        if (request is null)
        {
            return Usage(run);
        }

        var (setup, exit) = await PrepareAsync(run, needsCipher: request.WithPassword);
        if (setup is null)
        {
            return exit;
        }

        var id = run.Args[3];
        TenantSettingResult result;
        try
        {
            // The password is wrapped at once, so that no later line can print it.
            SecretValue? password = null;
            if (request.WithPassword)
            {
                var plain = run.Configuration[AdminCommands.SmtpPasswordSetting];
                if (string.IsNullOrEmpty(plain))
                {
                    plain = (await run.Input.ReadLineAsync(run.CancellationToken))?.TrimEnd('\r');
                }

                if (string.IsNullOrEmpty(plain))
                {
                    run.Error.WriteLine($"The SMTP password is required, in the environment variable {AdminCommands.SmtpPasswordSetting} or on standard input.");
                    return 2;
                }

                password = new SecretValue(plain);
            }

            await using var platform = NewContext(setup.Connection);
            result = await TenantEmailSettings.SetAsync(
                platform,
                setup.Cipher,
                run.Command.Name,
                id,
                request.Change with { Password = password },
                run.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return Print(run, id, "email", result);
    }

    public static async Task<int> EmailClearAsync(AdminRun run)
    {
        if (run.Args.Length != 4 || !IsName(run.Args[3]))
        {
            return Usage(run);
        }

        return await ChangeAsync(
            run,
            run.Args[3],
            "email",
            (platform, command) => TenantEmailSettings.ClearAsync(platform, command, run.Args[3], run.CancellationToken));
    }

    public static Task<int> HostAddAsync(AdminRun run) => HostAsync(run, TenantHostCommands.AddAsync);

    public static Task<int> HostRemoveAsync(AdminRun run) => HostAsync(run, TenantHostCommands.RemoveAsync);

    public static async Task<int> HostMoveAsync(AdminRun run)
    {
        if (run.Args.Length != 6 || !IsName(run.Args[3]) || run.Args[4] != "--to" || !IsName(run.Args[5]))
        {
            return Usage(run);
        }

        return await ChangeAsync(
            run,
            run.Args[5],
            "host",
            (platform, command) => TenantHostCommands.MoveAsync(platform, command, run.Args[3], run.Args[5], run.CancellationToken));
    }

    private static async Task<int> HostAsync(
        AdminRun run,
        Func<PlatformDbContext, string, string, string, CancellationToken, Task<TenantSettingResult>> change)
    {
        if (run.Args.Length != 5 || !IsName(run.Args[3]) || !IsName(run.Args[4]))
        {
            return Usage(run);
        }

        return await ChangeAsync(run, run.Args[3], "host", (platform, command) => change(platform, command, run.Args[3], run.Args[4], run.CancellationToken));
    }

    /// <summary>Runs a change that needs no secret: the source check, the settings, the tables check, the library call and the line.</summary>
    private static async Task<int> ChangeAsync(
        AdminRun run,
        string id,
        string what,
        Func<PlatformDbContext, string, Task<TenantSettingResult>> change)
    {
        var (setup, exit) = await PrepareAsync(run, needsCipher: false);
        if (setup is null)
        {
            return exit;
        }

        TenantSettingResult result;
        try
        {
            await using var platform = NewContext(setup.Connection);
            result = await change(platform, run.Command.Name);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return Print(run, id, what, result);
    }

    /// <summary>
    /// Checks the source and reads the settings and the platform tables, in that order. When the command cannot run, the setup is
    /// <see langword="null"/>, the reason is written and the exit code is returned.
    /// </summary>
    private static async Task<(Setup? Setup, int Exit)> PrepareAsync(AdminRun run, bool needsCipher)
    {
        // A setting's own message holds its value, so this says only which setting is wrong.
        TenancySource source;
        try
        {
            source = TenancySettings.Source(run.Configuration);
        }
        catch (InvalidOperationException)
        {
            run.Error.WriteLine($"The setting {TenancySettings.SourceSetting} must be 'configuration' or 'directory'.");
            return (null, 2);
        }

        if (source != TenancySource.Directory)
        {
            run.Error.WriteLine("The tenant setting commands need Tenancy:Source=directory.");
            return (null, 2);
        }

        string connection;
        TenantSecretCipher? cipher = null;
        if (needsCipher)
        {
            var platformSettings = AdminCommands.ReadPlatformSettings(run.Configuration, out var problem);
            if (platformSettings is null)
            {
                run.Error.WriteLine(problem);
                return (null, 2);
            }

            connection = platformSettings.PlatformConnectionString;
            cipher = platformSettings.Cipher;
        }
        else
        {
            var read = AdminCommands.ReadDirectorySettings(run.Configuration, out var problem);
            if (read is null)
            {
                run.Error.WriteLine(problem);
                return (null, 2);
            }

            connection = read;
        }

        try
        {
            var tablesProblem = await AdminCommands.PlatformTablesProblemAsync(connection, run.CancellationToken);
            if (tablesProblem is not null)
            {
                run.Error.WriteLine(tablesProblem);
                return (null, 2);
            }
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return (null, 1);
        }

        return (new Setup(connection, cipher), 0);
    }

    /// <summary>Reads the secret from <c>AXIS_BINDING_SECRET</c>, or from standard input when the variable is not set. Returns null when it is empty.</summary>
    private static async Task<SecretValue?> ReadSecretAsync(AdminRun run)
    {
        var value = run.Configuration[AdminCommands.BindingSecretSetting];
        if (string.IsNullOrEmpty(value))
        {
            value = (await run.Input.ReadToEndAsync(run.CancellationToken)).TrimEnd('\r', '\n');
        }

        return value.Length == 0 ? null : new SecretValue(value);
    }

    private sealed record EmailSetRequest(bool WithPassword, TenantEmailChange Change);

    // Duplicates, unknown options and a missing value are all usage errors. The usage text never echoes an argument.
    private static EmailSetRequest? ParseEmailSet(string[] args)
    {
        if (args.Length < 4 || !IsName(args[3]))
        {
            return null;
        }

        string? host = null;
        string? from = null;
        string? userName = null;
        int? port = null;
        bool? useTls = null;
        var withPassword = false;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--password")
            {
                if (withPassword)
                {
                    return null;
                }

                withPassword = true;
                continue;
            }

            if (i + 1 >= args.Length || !IsName(args[i + 1]))
            {
                return null;
            }

            var value = args[++i];
            switch (args[i - 1])
            {
                case "--host" when host is null:
                    host = value;
                    break;
                case "--from" when from is null:
                    from = value;
                    break;
                case "--user-name" when userName is null:
                    userName = value;
                    break;
                case "--port" when port is null:
                    if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return null;
                    }

                    port = parsed;
                    break;
                case "--tls" when useTls is null:
                    if (!bool.TryParse(value, out var tls))
                    {
                        return null;
                    }

                    useTls = tls;
                    break;
                default:
                    return null;
            }
        }

        return host is null || from is null
            ? null
            : new EmailSetRequest(withPassword, new TenantEmailChange(host, port, useTls, userName, null, from));
    }

    private static int Print(AdminRun run, string id, string what, TenantSettingResult result)
    {
        switch (result.Outcome)
        {
            case TenantSettingOutcome.Changed:
                run.Output.WriteLine($"{id} {what} ok");
                return 0;
            case TenantSettingOutcome.Refused:
                run.Error.WriteLine(result.Message);
                return 2;
            case TenantSettingOutcome.AuditFailed:
                run.Output.WriteLine($"{id} audit failed: {result.Message}");
                return 1;
            default:
                run.Output.WriteLine($"{id} {what} failed: {result.Message}");
                return 1;
        }
    }

    private static string Value(TenantBindingView binding) =>
        binding.Kind == DeploymentBindingKinds.Secret
            ? binding.HasSecret ? "(hidden)" : "(not set)"
            : binding.Url ?? "";

    private static bool IsName(string argument) => !argument.StartsWith("--", StringComparison.Ordinal);

    private static int Usage(AdminRun run)
    {
        run.Error.WriteLine(AdminCommands.Usage);
        return 2;
    }

    private static PlatformDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connectionString).Options);
}
