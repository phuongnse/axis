using System.Globalization;
using Axis.Administration;
using Axis.Configuration.Realms;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant binding list</c>, <c>set</c> and <c>remove</c> and <c>tenant host add</c>, <c>remove</c> and <c>move</c>, and
/// <c>tenant email set</c> and <c>clear</c>, and <c>tenant session end</c>. The rules are in <c>Axis.Administration</c>. This class parses the arguments, reads
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
                (run.Configuration[AdminCommands.DotnetEnvironmentSetting] ?? Environments.Production) == Environments.Development,
                run.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return Print(run, id, "email", result);
    }

    public static async Task<int> SignInSetAsync(AdminRun run)
    {
        var change = ParseSignInSet(run.Args);
        if (change is null)
        {
            return Usage(run);
        }

        // The key decrypts the schema password, to read the active releases, and the binding values, for the realm sync.
        var (setup, exit) = await PrepareAsync(run, needsCipher: true);
        if (setup is null)
        {
            return exit;
        }

        var id = run.Args[3];
        TenantRow? row;
        IReadOnlyDictionary<string, TenantBinding> bindings = new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase);
        RealmPlan? plan = null;
        try
        {
            await using var platform = NewContext(setup.Connection);
            row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id, run.CancellationToken);

            // An unknown tenant, a preview tenant and a change that is not well formed go straight to the library, which refuses them.
            if (row is { PreviewOf: null } && TenantPlatformSignIn.Problem(change) is null)
            {
                // A secret binding without a value counts as missing.
                var rows = await platform.TenantBindings.AsNoTracking().Where(binding => binding.TenantId == id).ToListAsync(run.CancellationToken);
                bindings = DirectoryTenantCatalog.Bindings(
                    id, rows.Where(binding => binding.Kind != DeploymentBindingKinds.Secret || binding.SecretValue is { Length: > 0 }), setup.Cipher!);

                // The tenant of a restore or a create may have no tables to read yet: it checks against no releases.
                var applications = row.SchemaVersion == "" ? [] : await ReadApplicationsAsync(setup, row, run.CancellationToken);
                var (problem, planned) = TenantPlatformSignIn.PlanRealm(id, change, bindings, applications, DirectoryTenantCatalog.Realm(row, setup.Cipher!) is not null);
                if (problem is not null)
                {
                    run.Error.WriteLine(problem);
                    return 2;
                }

                plan = planned;
            }
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            run.Output.WriteLine($"{id} sign-in failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }

        TenantSettingResult result;
        try
        {
            await using var platform = NewContext(setup.Connection);
            result = await TenantPlatformSignIn.SetAsync(platform, run.Command.Name, id, change, run.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        if (result.Outcome != TenantSettingOutcome.Changed || plan is null || DirectoryTenantCatalog.Realm(row!, setup.Cipher!) is not { } realm)
        {
            return Print(run, id, "sign-in", result);
        }

        // The stored sign-in stays when the identity provider fails: the next activation brings the realm in step.
        try
        {
            using var http = AdminCommands.IdentityProviderClient(run.IdentityProviderHandler);
            await ActivationRealmSync.SyncAsync(http, realm, bindings, plan, run.CancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
        {
            run.Output.WriteLine($"{id} realm failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }

        return Print(run, id, "sign-in", result);
    }

    // The tenant's active releases as the realm planner reads them.
    private static async Task<List<RealmApplication>> ReadApplicationsAsync(Setup setup, TenantRow row, CancellationToken cancellationToken)
    {
        var applications = new List<RealmApplication>();
        await using var steps = new TenantDatabaseSteps(setup.Cipher!, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(setup.Connection), cancellationToken);
        var connection = await steps.SchemaConnectionAsync();
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        var store = new ActiveReleaseStore(configuration);
        foreach (var active in await store.ListAsync(cancellationToken))
        {
            if (await store.GetReleaseAsync(active.ReleaseId, cancellationToken) is { } stored)
            {
                applications.Add(RealmApplication.FromRelease(active.Name, stored));
            }
        }

        return applications;
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

    public static async Task<int> SessionEndAsync(AdminRun run)
    {
        var args = run.Args;
        var isUser = args.Length == 6 && args[4] == "--user" && !string.IsNullOrWhiteSpace(args[5]) && IsName(args[5]);
        var isAll = args.Length == 5 && args[4] == "--all";
        if (!(isUser || isAll) || !IsName(args[3]))
        {
            return Usage(run);
        }

        var (setup, exit) = await PrepareAsync(run, needsCipher: true);
        if (setup is null)
        {
            return exit;
        }

        var id = args[3];
        var user = isUser ? args[5] : null;
        TenantRow? row;
        try
        {
            await using var platform = NewContext(setup.Connection);
            row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id, run.CancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            run.Error.WriteLine($"The setting {AdminCommands.PlatformSetting} could not be used to read the tenant directory.");
            return 1;
        }

        if (row is null)
        {
            run.Error.WriteLine("The tenant does not exist.");
            return 2;
        }

        if (row.Status == "provisioning")
        {
            run.Error.WriteLine($"The tenant '{id}' is still provisioning. Finish it with tenant create.");
            return 2;
        }

        // The record comes first: it and the sessions are in different databases, so they cannot share a transaction.
        if (!await new TenantAudit(run.Command.Name, setup.Connection).WriteAsync(id, run.Output, run.CancellationToken, [user is null ? "all" : $"user:{user}"]))
        {
            return 1;
        }

        try
        {
            await using var steps = new TenantDatabaseSteps(setup.Cipher!, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(setup.Connection), run.CancellationToken);
            var connection = await steps.SchemaConnectionAsync();
            var ended = user is null
                ? await SessionRevocation.EndAllAsync(connection, run.CancellationToken)
                : await SessionRevocation.EndForUserAsync(connection, user, run.CancellationToken);
            run.Output.WriteLine($"{id} sessions ok: {ended} ended");
            return 0;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            run.Output.WriteLine($"{id} sessions failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }
    }

    public static async Task<int> HostAddAsync(AdminRun run)
    {
        var args = run.Args;
        var withSite = args.Length == 7 && args[5] == "--site" && IsName(args[6]);
        if (!(args.Length == 5 || withSite) || !IsName(args[3]) || !IsName(args[4]))
        {
            return Usage(run);
        }

        var sitePath = withSite ? args[6] : null;
        return await ChangeAsync(
            run,
            args[3],
            "host",
            (platform, command) => TenantHostCommands.AddAsync(platform, command, args[3], args[4], sitePath, run.CancellationToken));
    }

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

    private sealed class ProviderBuilder(string name)
    {
        public string Name { get; } = name;

        public string? Type { get; set; }

        public string? Issuer { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public string? ClientKey { get; set; }

        public string? Metadata { get; set; }

        public string? RolesClaim { get; set; }

        public List<KeyValuePair<string, string>> Claims { get; } = [];

        public List<KeyValuePair<string, string>> RoleValues { get; } = [];

        public TenantPlatformProvider? Build() =>
            Type is null
                ? null
                : new TenantPlatformProvider(Name, Type, Issuer, ClientId, ClientSecret, ClientKey, Metadata, Claims, RolesClaim, RoleValues);
    }

    // Duplicates, unknown options, a missing value and a provider option before any --provider are all usage errors. The
    // usage text never echoes an argument. The rules about the values are in TenantPlatformSignIn.
    private static TenantPlatformSignInChange? ParseSignInSet(string[] args)
    {
        if (args.Length < 4 || !IsName(args[3]))
        {
            return null;
        }

        bool? accounts = null;
        var builders = new List<ProviderBuilder>();
        for (var i = 4; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || !IsName(args[i + 1]))
            {
                return null;
            }

            var option = args[i];
            var value = args[++i];
            if (option == "--accounts")
            {
                if (accounts is not null || !bool.TryParse(value, out var parsed))
                {
                    return null;
                }

                accounts = parsed;
                continue;
            }

            if (option == "--provider")
            {
                builders.Add(new ProviderBuilder(value));
                continue;
            }

            if (builders.Count == 0)
            {
                return null;
            }

            var provider = builders[^1];
            switch (option)
            {
                case "--type" when provider.Type is null:
                    provider.Type = value;
                    break;
                case "--issuer" when provider.Issuer is null:
                    provider.Issuer = value;
                    break;
                case "--client-id" when provider.ClientId is null:
                    provider.ClientId = value;
                    break;
                case "--client-secret" when provider.ClientSecret is null:
                    provider.ClientSecret = value;
                    break;
                case "--client-key" when provider.ClientKey is null:
                    provider.ClientKey = value;
                    break;
                case "--metadata" when provider.Metadata is null:
                    provider.Metadata = value;
                    break;
                case "--roles-claim" when provider.RolesClaim is null:
                    provider.RolesClaim = value;
                    break;
                case "--claim" or "--role":
                    var separator = value.IndexOf('=', StringComparison.Ordinal);
                    if (separator < 1)
                    {
                        return null;
                    }

                    (option == "--claim" ? provider.Claims : provider.RoleValues).Add(new(value[..separator], value[(separator + 1)..]));
                    break;
                default:
                    return null;
            }
        }

        var providers = builders.Select(builder => builder.Build()).ToList();
        return accounts is null || providers.Contains(null)
            ? null
            : new TenantPlatformSignInChange(accounts.Value, providers!);
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
