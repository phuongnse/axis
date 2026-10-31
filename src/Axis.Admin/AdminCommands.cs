using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Administration;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Data;
using Axis.Data.Records;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The commands of the admin host. Exit code 0 means the command succeeded, 1 that a step failed and 2 that nothing ran:
/// a usage or settings error. Outside Development and Testing, a command refuses a connection setting below <c>SSL Mode=VerifyFull</c>.
/// A message that names a setting never holds its value.
/// </summary>
internal static partial class AdminCommands
{
    public const string AdminSetting = "Admin:ConnectionString";

    public const string PlatformSetting = "ConnectionStrings:PlatformSchema";

    public const string InlineRecomputeLimitSetting = "Activation:InlineRecomputeLimit";

    public const string ScenarioServerSetting = "Scenarios:ServerConnectionString";

    public const string DotnetEnvironmentSetting = "DOTNET_ENVIRONMENT";

    public const string IdentityProviderUrlSetting = "IdentityProvider:Url";

    public const string IdentityProviderAdminClientIdSetting = "IdentityProvider:AdminClientId";

    public const string IdentityProviderAdminClientSecretSetting = "IdentityProvider:AdminClientSecret";

    private const string Provisioning = "provisioning";

    private const string ActiveStatus = "active";

    public const string ClientSecretSetting = "AXIS_OIDC_CLIENT_SECRET";

    public const string ClientKeySetting = "AXIS_OIDC_CLIENT_KEY";

    public const string BindingSecretSetting = "AXIS_BINDING_SECRET";

    public const string SmtpPasswordSetting = "AXIS_SMTP_PASSWORD";

    public const string PathSetting = "PATH";

    internal const string Usage =
        """
        Usage: tenant create <id> --host <host>... [--administrator <email>]
               tenant create <id> --preview-of <main> --host <host>... [--binding <name>=<value>]...
               tenant backup <id> --out <file> --backup-key-file <file>
               tenant restore <file> --tenant <id> --host <host>... --backup-key-file <file>
               migrate [--tenant <id>] [--parallel <n>]
               activate <folder> [--tenant <id>]
               tenant suspend <id>
               tenant resume <id>
               tenant rotate-passwords <id>
               tenant oidc set <id> [--endpoint-audience <value>] [--service-audience <value>] [--profile standard|fapi2] [--client-key] [--retire-client-key <kid>]
               tenant sign-in set <id> --accounts true|false [--provider <name> --type oidc|saml [--issuer <binding>] [--client-id <binding>] [--client-secret <binding>] [--client-key <binding>] [--metadata <binding>] [--claim <name>=<claim>]... [--roles-claim <claim>] [--role <value>=<role>]...]...
               tenant email set <id> --host <host> --from <address> [--port <n>] [--tls true|false] [--user-name <name>] [--password]
               tenant email clear <id>
               tenant session end <id> --user <user>
               tenant session end <id> --all
               tenant list
               tenant binding list <id>
               tenant binding set <id> <name> --url <value>
               tenant binding set <id> <name> --storage database
               tenant binding set <id> <name> --secret
               tenant binding remove <id> <name>
               tenant host add <id> <host> [--site <path>]
               tenant host remove <id> <host>
               tenant host move <host> --to <id>
               platform rekey
               publisher
               version
        """;

    public static Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(args, configuration, TextReader.Null, output, error, cancellationToken);

    public static Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(args, configuration, input, output, error, Publisher.DefaultLeaseDuration, Publisher.DefaultTestLimit, null, cancellationToken);

    // The publisher lease, the test limit and the identity provider handler are parameters so that tests can use short ones and a stub.
    // The product has no setting for them.
    internal static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextReader input,
        TextWriter output,
        TextWriter error,
        TimeSpan publisherLease,
        TimeSpan publisherTestLimit,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var command = Commands.FirstOrDefault(row => Matches(row, args));
        if (command is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        // Checked before any command runs, so no command opens a connection without full certificate checks.
        var environment = new AdminEnvironment(configuration[DotnetEnvironmentSetting] ?? Environments.Production);
        if (DatabaseTlsRequirement.Applies(environment))
        {
            foreach (var setting in new[] { AdminSetting, PlatformSetting, ScenarioServerSetting })
            {
                var problem = DatabaseTlsRequirement.Problem(setting, configuration[setting]);
                if (problem is not null)
                {
                    error.WriteLine(problem);
                    return 2;
                }
            }
        }

        return await command.RunAsync(new AdminRun(command, args, configuration, input, output, error, publisherLease, publisherTestLimit, identityProviderHandler, cancellationToken));
    }

    /// <summary>
    /// The commands of the host. The dispatcher picks the first row whose name is the leading arguments, and a test reads every row's
    /// audit mark, so a command without one fails it.
    /// </summary>
    internal static IReadOnlyList<AdminCommand> Commands { get; } =
    [
        new("tenant create", AuditMark.OnePerTenant, run => CreateAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.IdentityProviderHandler, run.CancellationToken)),
        new("tenant backup", AuditMark.OnePerTenant, run => BackupAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("tenant restore", AuditMark.OnePerTenant, run => RestoreAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.IdentityProviderHandler, run.CancellationToken)),
        new("tenant suspend", AuditMark.OnePerTenant, run => StatusAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("tenant resume", AuditMark.OnePerTenant, run => StatusAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("tenant rotate-passwords", AuditMark.OnePerTenant, run => RotatePasswordsAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("tenant oidc set", AuditMark.OnePerTenant, run => OidcSetAsync(run.Command.Name, run.Args, run.Configuration, run.Input, run.Output, run.Error, run.IdentityProviderHandler, run.CancellationToken)),
        new("tenant sign-in set", AuditMark.OnePerTenant, TenantSettings.SignInSetAsync),
        new("tenant email set", AuditMark.OnePerTenant, TenantSettings.EmailSetAsync),
        new("tenant email clear", AuditMark.OnePerTenant, TenantSettings.EmailClearAsync),
        new("tenant session end", AuditMark.OnePerTenant, TenantSettings.SessionEndAsync),
        new("tenant list", AuditMark.None, run => StatusAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("tenant binding list", AuditMark.None, TenantSettings.BindingListAsync),
        new("tenant binding set", AuditMark.OnePerTenant, TenantSettings.BindingSetAsync),
        new("tenant binding remove", AuditMark.OnePerTenant, TenantSettings.BindingRemoveAsync),
        new("tenant host add", AuditMark.OnePerTenant, TenantSettings.HostAddAsync),
        new("tenant host remove", AuditMark.OnePerTenant, TenantSettings.HostRemoveAsync),
        new("tenant host move", AuditMark.OnePerTenant, TenantSettings.HostMoveAsync),
        new("migrate", AuditMark.OnePerTenant, run => MigrateAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("activate", AuditMark.OnePerTenant, run => ActivateAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.IdentityProviderHandler, run.CancellationToken)),
        new("platform rekey", AuditMark.OnePerRun, run => RekeyAsync(run.Command.Name, run.Args, run.Configuration, run.Output, run.Error, run.CancellationToken)),
        new("publisher", AuditMark.None, run => PublisherAsync(run.Args, run.Configuration, run.Output, run.Error, run.PublisherLease, run.PublisherTestLimit, run.IdentityProviderHandler, run.CancellationToken)),
        new("version", AuditMark.None, VersionAsync),
    ];

    private static bool Matches(AdminCommand command, string[] args)
    {
        var words = command.Name.Split(' ');
        return args.Length >= words.Length && words.Select((word, index) => args[index] == word).All(equal => equal);
    }

    private static Task<int> VersionAsync(AdminRun run)
    {
        if (run.Args.Length != 1)
        {
            run.Error.WriteLine(Usage);
            return Task.FromResult(2);
        }

        run.Output.WriteLine(typeof(AdminCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        return Task.FromResult(0);
    }

    private static async Task<int> CreateAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        var request = ParseTenantCreate(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadCreateSettings(configuration, readClientSecret: false, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        // A preview tenant gets no realm, so it needs none of the identity provider settings.
        if (request.PreviewOf is null)
        {
            var environment = new AdminEnvironment(configuration[DotnetEnvironmentSetting] ?? Environments.Production);
            var identityProvider = ReadIdentityProvider(configuration, environment, out problem);
            if (identityProvider is null)
            {
                error.WriteLine(problem);
                return 2;
            }

            if (ReadRealmMail(configuration, out problem) is not { } mail)
            {
                error.WriteLine(problem);
                return 2;
            }

            settings = settings with { IdentityProvider = identityProvider, Mail = mail };
        }

        try
        {
            var refusal = await ValidateAsync(settings, request, cancellationToken);
            if (refusal is not null)
            {
                error.WriteLine(refusal);
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        using var http = IdentityProviderClient(identityProviderHandler);
        var realm = settings.IdentityProvider is null ? null : new TenantRealm(http, settings.IdentityProvider);
        return await new TenantCreate(settings, request, realm, http, new TenantAudit(command, settings.PlatformConnectionString), command, output, cancellationToken).RunAsync();
    }

    // The client for the identity provider. A test passes its own handler, which the client does not dispose.
    internal static HttpClient IdentityProviderClient(HttpMessageHandler? handler) =>
        handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(60) }
            : new HttpClient(handler, disposeHandler: false);

    private static async Task<int> RekeyAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            error.WriteLine(Usage);
            return 2;
        }

        // The rekey works under both tenancy sources, so it never reads Tenancy:Source.
        var settings = ReadPlatformSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        if (!settings.Cipher.HasPreviousKey)
        {
            error.WriteLine($"The setting {TenantSecretCipher.PreviousKeySetting} is required.");
            return 2;
        }

        try
        {
            var tablesProblem = await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);
            if (tablesProblem is not null)
            {
                error.WriteLine(tablesProblem);
                return 2;
            }

            var result = await PlatformRekey.RunAsync(settings.PlatformConnectionString, settings.Cipher, command, cancellationToken);
            if (result.Problem is not null)
            {
                error.WriteLine(result.Problem);
                return result.Exit;
            }

            output.WriteLine($"platform rekey ok: {result.TenantSecrets} tenant secrets, {result.DataProtectionKeys} data protection keys");
            return 0;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any change.");
            return 1;
        }
    }

    private static async Task<int> StatusAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var shapeIsValid = args[1] == "list" ? args.Length == 2 : args.Length == 3 && !args[2].StartsWith("--", StringComparison.Ordinal);
        if (!shapeIsValid)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var connectionString = ReadDirectorySettings(configuration, out var problem);
        if (connectionString is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        try
        {
            var tablesProblem = await PlatformTablesProblemAsync(connectionString, cancellationToken);
            if (tablesProblem is not null)
            {
                error.WriteLine(tablesProblem);
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return args[1] switch
        {
            "suspend" => await TenantStatus.SuspendAsync(connectionString, args[2], new TenantAudit(command, connectionString), output, error, cancellationToken),
            "resume" => await TenantStatus.ResumeAsync(connectionString, args[2], new TenantAudit(command, connectionString), output, error, cancellationToken),
            _ => await TenantStatus.ListAsync(connectionString, output, error, cancellationToken),
        };
    }

    /// <summary>
    /// <c>tenant rotate-passwords</c>. Everything that can be refused is checked before the audit record, so a refusal changes
    /// nothing. The rules of the rotation are in <see cref="TenantPasswordRotation"/>.
    /// </summary>
    private static async Task<int> RotatePasswordsAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            error.WriteLine(Usage);
            return 2;
        }

        // A setting's own message holds its value, so this says only which setting is wrong.
        TenancySource source;
        try
        {
            source = TenancySettings.Source(configuration);
        }
        catch (InvalidOperationException)
        {
            error.WriteLine($"The setting {TenancySettings.SourceSetting} must be 'configuration' or 'directory'.");
            return 2;
        }

        if (source != TenancySource.Directory)
        {
            error.WriteLine("This command needs Tenancy:Source=directory.");
            return 2;
        }

        var settings = ReadCreateSettings(configuration, readClientSecret: false, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var id = args[2];
        TenantRow? row;
        try
        {
            var refusal = await AdminRoleProblemAsync(settings, cancellationToken)
                ?? await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);
            if (refusal is not null)
            {
                error.WriteLine(refusal);
                return 2;
            }

            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
            row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            error.WriteLine($"The setting {PlatformSetting} could not be used to read the tenant directory.");
            return 2;
        }

        if (row is null)
        {
            error.WriteLine("The tenant does not exist.");
            return 2;
        }

        if (row.Status == Provisioning)
        {
            error.WriteLine($"The tenant '{id}' is still provisioning. Finish it with tenant create.");
            return 2;
        }

        if (!await new TenantAudit(command, settings.PlatformConnectionString).WriteAsync(id, output, cancellationToken, [row.RuntimeUser, row.SchemaUser]))
        {
            return 1;
        }

        try
        {
            await using var admin = new NpgsqlConnection(settings.Admin.ConnectionString);
            await admin.OpenAsync(cancellationToken);

            // The runtime role first, so that the server gets its new password as early as possible.
            return await RotateStepAsync(id, "runtime-password", () => TenantPasswordRotation.RotateRuntimeAsync(settings.Cipher, settings.PlatformConnectionString, admin, row, cancellationToken), output)
                && await RotateStepAsync(id, "schema-password", () => TenantPasswordRotation.RotateSchemaAsync(settings.Cipher, settings.PlatformConnectionString, admin, row, cancellationToken), output)
                ? 0
                : 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            output.WriteLine($"{id} runtime-password failed: {TenantDatabaseSteps.Safe(exception)}");
            return 1;
        }
    }

    private static async Task<bool> RotateStepAsync(string id, string step, Func<Task> rotate, TextWriter output)
    {
        try
        {
            await rotate();
            output.WriteLine($"{id} {step} ok");
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            output.WriteLine($"{id} {step} failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }
    }

    /// <summary>
    /// <c>tenant oidc set</c>. This only parses, reads the client key and prints. The rules are in <see cref="TenantSignInSettings"/>,
    /// which refuses before it writes the audit record. No message echoes an argument. A client key comes from
    /// <see cref="ClientKeySetting"/> or the whole of standard input, never from an argument.
    /// </summary>
    private static async Task<int> OidcSetAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextReader input,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        var request = ParseOidcSet(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        TenancySource source;
        try
        {
            source = TenancySettings.Source(configuration);
        }
        catch (InvalidOperationException)
        {
            error.WriteLine($"The setting {TenancySettings.SourceSetting} must be 'configuration' or 'directory'.");
            return 2;
        }

        if (source != TenancySource.Directory)
        {
            error.WriteLine("This command needs Tenancy:Source=directory.");
            return 2;
        }

        var settings = ReadPlatformSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var id = request.Id;
        try
        {
            var tablesProblem = await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);
            if (tablesProblem is not null)
            {
                error.WriteLine(tablesProblem);
                return 2;
            }

            var change = request.Change;
            if (request.WithClientKey)
            {
                var keyVariable = configuration[ClientKeySetting];
                var key = string.IsNullOrEmpty(keyVariable) ? await input.ReadToEndAsync(cancellationToken) : keyVariable;
                change = change with { ClientKey = key };
            }

            using var http = IdentityProviderClient(identityProviderHandler);
            var result = await TenantSignInSettings.SetAsync(settings.Cipher, settings.PlatformConnectionString, command, id, change, http, cancellationToken);
            switch (result.Outcome)
            {
                case TenantSignInOutcome.Refused:
                    error.WriteLine(result.Refusal);
                    return 2;
                case TenantSignInOutcome.AuditFailed:
                    // A save failure wraps the database error, whose message is the one that helps the operator.
                    var cause = result.AuditError is DbUpdateException { InnerException: { } inner } ? inner : result.AuditError!;
                    output.WriteLine($"{id} audit failed: {TenantDatabaseSteps.Safe(cause)}");
                    return 1;
                case TenantSignInOutcome.RealmFailed:
                    output.WriteLine($"{id} realm failed: {TenantDatabaseSteps.Safe(result.RealmError!)}");
                    return 1;
                default:
                    output.WriteLine($"{id} oidc ok");
                    if (result.AddedKeyId is not null)
                    {
                        output.WriteLine($"{id} client-key {result.AddedKeyId}");
                        foreach (var line in result.AddedPublicKeyPem!.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
                        {
                            output.WriteLine(line);
                        }
                    }

                    return 0;
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or InvalidOperationException or OperationCanceledException)
        {
            var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
            output.WriteLine($"{id} oidc failed: {TenantDatabaseSteps.Safe(cause)}");
            return 1;
        }
    }

    private static OidcSetRequest? ParseOidcSet(string[] args)
    {
        if (args.Length < 4 || args[3].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var withKey = false;
        string? retireKeyId = null;
        string? endpointAudience = null;
        string? serviceAudience = null;
        string? profile = null;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--client-key")
            {
                if (withKey)
                {
                    return null;
                }

                withKey = true;
                continue;
            }

            // A value may be empty, which removes an audience, but is never a second option. A key id is base64url
            // text, so it can start with "--" and still be a value.
            if (i + 1 >= args.Length || _oidcSetOptions.Contains(args[i + 1]))
            {
                return null;
            }

            var value = args[++i];
            switch (args[i - 1])
            {
                case "--endpoint-audience" when endpointAudience is null:
                    endpointAudience = value;
                    break;
                case "--service-audience" when serviceAudience is null:
                    serviceAudience = value;
                    break;
                case "--profile" when profile is null:
                    profile = value;
                    break;
                case "--retire-client-key" when retireKeyId is null:
                    retireKeyId = value;
                    break;
                default:
                    return null;
            }
        }

        return new OidcSetRequest(
            args[3],
            withKey,
            new TenantSignInChange(endpointAudience, serviceAudience, profile, null, retireKeyId));
    }

    private static readonly string[] _oidcSetOptions =
    [
        "--client-key",
        "--endpoint-audience",
        "--service-audience",
        "--profile",
        "--retire-client-key",
    ];

    private static async Task<int> BackupAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var request = ParseTenantBackup(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadBackupSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var backupKey = BackupKeyFile.Read(request.BackupKeyFile, out problem);
        if (backupKey is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        BackupPlan? plan;
        try
        {
            plan = await TenantBackup.PrepareAsync(settings, request, backupKey, error, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return plan is null
            ? 2
            : await new TenantBackup(settings, plan, new TenantAudit(command, settings.PlatformConnectionString), output, cancellationToken).RunAsync();
    }

    private static async Task<int> RestoreAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        var request = ParseTenantRestore(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadRestoreSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var backupKey = BackupKeyFile.Read(request.BackupKeyFile, out problem);
        if (backupKey is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var pgRestore = FindTool(settings.SearchPath, TenantRestore.PgRestore);
        if (pgRestore is null)
        {
            error.WriteLine($"{TenantRestore.PgRestore} was not found in the directories of {PathSetting}.");
            return 2;
        }

        using var http = IdentityProviderClient(identityProviderHandler);
        var realm = settings.Create.IdentityProvider is null ? null : new TenantRealm(http, settings.Create.IdentityProvider);
        RestorePlan? plan;
        try
        {
            var refusal = await ValidateRestoreAsync(settings.Create, request, cancellationToken);
            if (refusal is not null)
            {
                error.WriteLine(refusal);
                return 2;
            }

            plan = await TenantRestore.PrepareAsync(settings, request, backupKey, pgRestore, realm, output, error, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return plan is null
            ? 2
            : await new TenantRestore(settings.Create, plan, realm, http, new TenantAudit(command, settings.Create.PlatformConnectionString), output, error, cancellationToken).RunAsync();
    }

    private static TenantRestoreRequest? ParseTenantRestore(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var hosts = new List<string>();
        string? id = null;
        string? keyFile = null;
        for (var i = 3; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            var value = args[i + 1];
            switch (args[i])
            {
                case "--host":
                    hosts.Add(value);
                    break;
                case "--tenant" when id is null:
                    id = value;
                    break;
                case BackupKeyFile.Option when keyFile is null:
                    keyFile = value;
                    break;
                default:
                    return null;
            }
        }

        return hosts.Count == 0 || id is null || keyFile is null ? null : new TenantRestoreRequest(args[2], id, hosts, keyFile);
    }

    private static TenantBackupRequest? ParseTenantBackup(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        string? file = null;
        string? keyFile = null;
        for (var i = 3; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return null;
            }

            switch (args[i])
            {
                case "--out" when file is null:
                    file = args[i + 1];
                    break;
                case BackupKeyFile.Option when keyFile is null:
                    keyFile = args[i + 1];
                    break;
                default:
                    return null;
            }
        }

        return file is null || keyFile is null ? null : new TenantBackupRequest(args[2], file, keyFile);
    }

    private static TenantCreateRequest? ParseTenantCreate(string[] args)
    {
        if (args.Length < 3 || args[0] != "tenant" || args[1] != "create" || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var hosts = new List<string>();
        var bindings = new List<KeyValuePair<string, string>>();
        string? previewOf = null;
        string? administrator = null;
        for (var i = 3; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            var value = args[i + 1];
            switch (args[i])
            {
                case "--host":
                    hosts.Add(value);
                    break;
                case "--preview-of" when previewOf is null:
                    previewOf = value;
                    break;
                case "--administrator" when administrator is null:
                    // The usage text never echoes an argument, so the email is never printed.
                    administrator = RealmUsers.NormalizeEmail(value);
                    if (administrator is null)
                    {
                        return null;
                    }

                    break;
                case "--binding":
                    // The usage text never echoes an argument, so a binding value is never printed.
                    var separator = value.IndexOf('=', StringComparison.Ordinal);
                    if (separator <= 0)
                    {
                        return null;
                    }

                    bindings.Add(new(value[..separator], value[(separator + 1)..]));
                    break;
                default:
                    return null;
            }
        }

        if (hosts.Count == 0)
        {
            return null;
        }

        // A binding is only for a preview tenant, and a preview tenant has no realm for an administrator.
        return (previewOf is null && bindings.Count > 0) || (previewOf is not null && administrator is not null)
            ? null
            : new TenantCreateRequest(args[2], hosts, previewOf, bindings, administrator);
    }

    private static async Task<int> MigrateAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var request = ParseMigrate(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        if (request.Parallel < 1)
        {
            error.WriteLine("--parallel must be 1 or more.");
            return 2;
        }

        var settings = ReadPlatformSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        IReadOnlyList<TenantRow> tenants;
        try
        {
            if (!await PlatformTablesExistAsync(settings.PlatformConnectionString, cancellationToken))
            {
                // With no directory there is no tenant to migrate, and the install runs this before the first server start.
                error.WriteLine("The platform tables do not exist, so there is no tenant to migrate.");
                return 0;
            }

            // An installation upgrades with this command before the new server starts, so the platform tables are brought up first.
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
            await platform.Database.MigrateAsync(cancellationToken);
            var rows = await platform.Tenants.AsNoTracking().ToListAsync(cancellationToken);
            tenants = [.. rows.OrderBy(row => row.Id, StringComparer.Ordinal)];
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (PostgresException exception)
        {
            error.WriteLine($"The setting {PlatformSetting} could not be used: {exception.MessageText}");
            return 2;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            error.WriteLine($"The setting {PlatformSetting} could not be used to connect to the platform database.");
            return 2;
        }

        if (request.Tenant is not null)
        {
            var found = tenants.FirstOrDefault(row => row.Id == request.Tenant);
            if (found is null)
            {
                error.WriteLine($"The tenant '{request.Tenant}' does not exist.");
                return 2;
            }

            if (found.Status == Provisioning)
            {
                error.WriteLine($"The tenant '{request.Tenant}' is still provisioning. Finish it with tenant create.");
                return 2;
            }

            tenants = [found];
        }
        else
        {
            tenants = [.. tenants.Where(row => row.Status != Provisioning)];
        }

        return await new TenantMigrate(settings, tenants, request.Parallel, new TenantAudit(command, settings.PlatformConnectionString), output, cancellationToken).RunAsync();
    }

    private static async Task<int> ActivateAsync(
        string command,
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        var request = ParseActivate(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadActivateSettings(configuration, error, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        var folder = Path.GetFullPath(request.Folder);
        if (!Directory.Exists(folder))
        {
            error.WriteLine($"The application folder '{request.Folder}' does not exist.");
            return 2;
        }

        IReadOnlyList<TenantRow> tenants;
        Dictionary<string, ActivationBindings> bindings;
        Dictionary<string, IReadOnlyDictionary<string, TenantBinding>> realmBindings;
        try
        {
            var tablesProblem = await PlatformTablesProblemAsync(settings.Platform.PlatformConnectionString, cancellationToken);
            if (tablesProblem is not null)
            {
                error.WriteLine(tablesProblem);
                return 2;
            }

            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.Platform.PlatformConnectionString).Options);
            var rows = await platform.Tenants.AsNoTracking().ToListAsync(cancellationToken);
            if (request.Tenant is not null)
            {
                var found = rows.FirstOrDefault(row => row.Id == request.Tenant);
                if (found is null)
                {
                    error.WriteLine($"The tenant '{request.Tenant}' does not exist.");
                    return 2;
                }

                if (found.Status == Provisioning)
                {
                    error.WriteLine($"The tenant '{request.Tenant}' is still provisioning. Finish it with tenant create.");
                    return 2;
                }

                tenants = [found];
            }
            else
            {
                tenants = [.. rows.Where(row => row.Status == ActiveStatus).OrderBy(row => row.Id, StringComparer.Ordinal)];
            }

            bindings = await ReadBindingsAsync(platform, [.. tenants.Select(row => row.Id)], cancellationToken);
            realmBindings = await ReadRealmBindingsAsync(platform, [.. tenants.Select(row => row.Id)], settings.Platform.Cipher, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }
        catch (PostgresException exception)
        {
            error.WriteLine($"The setting {PlatformSetting} could not be used: {exception.MessageText}");
            return 2;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            error.WriteLine($"The setting {PlatformSetting} could not be used to connect to the platform database.");
            return 2;
        }

        using var http = IdentityProviderClient(identityProviderHandler);
        return await new TenantActivate(
            settings.Platform,
            tenants,
            bindings,
            realmBindings,
            settings.Mail,
            http,
            folder,
            settings.Packages,
            settings.Modules,
            settings.InlineRecomputeLimit,
            settings.ActivationTimeout,
            new TenantAudit(command, settings.Platform.PlatformConnectionString),
            output,
            error,
            cancellationToken).RunAsync();
    }

    private static async Task<int> PublisherAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan lease,
        TimeSpan testLimit,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1)
        {
            error.WriteLine(Usage);
            return 2;
        }

        // A setting's own message holds its value, so these say only which setting is wrong.
        TenancySource source;
        try
        {
            source = TenancySettings.Source(configuration);
        }
        catch (InvalidOperationException)
        {
            error.WriteLine($"The setting {TenancySettings.SourceSetting} must be 'configuration' or 'directory'.");
            return 2;
        }

        return source == TenancySource.Directory
            ? await DirectoryPublisherAsync(configuration, output, error, lease, testLimit, identityProviderHandler, cancellationToken)
            : await ConfigurationPublisherAsync(configuration, output, error, lease, testLimit, identityProviderHandler, cancellationToken);
    }

    private static async Task<int> DirectoryPublisherAsync(
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan lease,
        TimeSpan testLimit,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        TimeSpan refreshInterval;
        try
        {
            refreshInterval = TenancySettings.RefreshInterval(configuration);
        }
        catch (InvalidOperationException)
        {
            error.WriteLine($"The setting {TenancySettings.RefreshIntervalSetting} must be a positive time span, such as 00:00:30.");
            return 2;
        }

        var settings = ReadActivateSettings(configuration, error, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        if (!TryReadScenarioServer(configuration, error, out var scenarioServer))
        {
            return 2;
        }

        using var http = IdentityProviderClient(identityProviderHandler);
        return await new Publisher(
            new ReleaseSettings(settings.Packages, settings.Modules, settings.InlineRecomputeLimit, settings.ActivationTimeout),
            scenarioServer,
            new DirectoryPublisherTenants(settings.Platform, settings.Mail, refreshInterval, http),
            new SensitiveValues(new TenantKeys(new DirectoryTenantKeySource(settings.Platform.PlatformConnectionString, settings.Platform.Cipher))),
            lease,
            testLimit,
            output,
            error,
            cancellationToken).RunAsync();
    }

    private static async Task<int> ConfigurationPublisherAsync(
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan lease,
        TimeSpan testLimit,
        HttpMessageHandler? identityProviderHandler,
        CancellationToken cancellationToken)
    {
        ConfigurationTenantCatalog catalog;
        try
        {
            catalog = ConfigurationTenantCatalog.Load(configuration, new AdminEnvironment(configuration[DotnetEnvironmentSetting] ?? Environments.Production));
        }
        catch (InvalidOperationException exception)
        {
            // The tenant check's messages name a setting or a tenant and never a value.
            error.WriteLine(exception.Message);
            return 2;
        }

        foreach (var entry in catalog.Tenants.Where(entry => entry.SchemaConnectionString is null))
        {
            error.WriteLine($"The setting {TenantOptions.SectionName}:{entry.Id}:SchemaConnectionString is required.");
            return 2;
        }

        var settings = ReadReleaseSettings(configuration, error, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        if (!TryReadScenarioServer(configuration, error, out var scenarioServer))
        {
            return 2;
        }

        if (ReadRealmMail(configuration, out problem) is not { } mail)
        {
            error.WriteLine(problem);
            return 2;
        }

        var allowHttp = (configuration[DotnetEnvironmentSetting] ?? Environments.Production) == Environments.Development;
        using var http = IdentityProviderClient(identityProviderHandler);
        return await new Publisher(
            settings,
            scenarioServer,
            new ConfigurationPublisherTenants(catalog.Tenants, mail, allowHttp, output, http),
            new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration))),
            lease,
            testLimit,
            output,
            error,
            cancellationToken).RunAsync();
    }

    // An empty value is no setting: a test request then fails with AXC0217. A value that is no connection string stops the
    // publisher, and the message names the setting and never its value.
    private static bool TryReadScenarioServer(IConfiguration configuration, TextWriter error, out string? scenarioServer)
    {
        scenarioServer = null;
        var value = configuration[ScenarioServerSetting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            error.WriteLine($"The setting {ScenarioServerSetting} is not a valid connection string.");
            return false;
        }

        scenarioServer = value;
        return true;
    }

    /// <summary>
    /// Reads the deployment bindings of <paramref name="ids"/> from the directory, as activation needs them. A secret's value
    /// stays encrypted: activation checks only that the binding exists.
    /// </summary>
    internal static async Task<Dictionary<string, ActivationBindings>> ReadBindingsAsync(
        PlatformDbContext platform,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var bindingRows = await platform.TenantBindings.AsNoTracking().Where(binding => ids.Contains(binding.TenantId)).ToListAsync(cancellationToken);
        var previewIds = await platform.Tenants.AsNoTracking()
            .Where(tenant => ids.Contains(tenant.Id) && tenant.PreviewOf != null)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
        var platformRows = await platform.TenantPlatformProviders.AsNoTracking().Where(provider => ids.Contains(provider.TenantId)).ToListAsync(cancellationToken);
        var bindings = new Dictionary<string, ActivationBindings>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var named = bindingRows
                .Where(binding => binding.TenantId == id)
                .ToDictionary(
                    binding => binding.Name,
                    binding => new ActivationBinding(
                        binding.Kind,
                        binding.Kind == DeploymentBindingKinds.Url ? new Uri(binding.Url!, UriKind.Absolute) : null),
                    StringComparer.OrdinalIgnoreCase);
            bindings[id] = new ActivationBindings(id, named, AllowHttp: false, IsPreview: previewIds.Contains(id))
            {
                PlatformProviders = PlatformSignInProviders.From(platformRows.Where(provider => provider.TenantId == id)),
            };
        }

        return bindings;
    }

    /// <summary>
    /// Reads the deployment bindings of <paramref name="ids"/> from the directory, as the realm sync needs them. A secret's value
    /// stays encrypted until the sync reveals it.
    /// </summary>
    internal static async Task<Dictionary<string, IReadOnlyDictionary<string, TenantBinding>>> ReadRealmBindingsAsync(
        PlatformDbContext platform,
        IReadOnlyCollection<string> ids,
        TenantSecretCipher cipher,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantBindings.AsNoTracking().Where(binding => ids.Contains(binding.TenantId)).ToListAsync(cancellationToken);
        return ids.ToDictionary(
            id => id,
            id => DirectoryTenantCatalog.Bindings(id, rows.Where(row => row.TenantId == id), cipher),
            StringComparer.Ordinal);
    }

    private static ActivateRequest? ParseActivate(string[] args)
    {
        string? folder = null;
        string? tenant = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--tenant")
            {
                if (tenant is not null || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return null;
                }

                tenant = args[++i];
            }
            else if (folder is null && !args[i].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[i]))
            {
                folder = args[i];
            }
            else
            {
                return null;
            }
        }

        return folder is null ? null : new ActivateRequest(folder, tenant);
    }

    /// <summary>
    /// Reads the settings of <c>activate</c>: the common ones, the extension package and module folders and the inline recompute limit.
    /// A package or module folder that is not set gives an empty catalog, as on the server. One that is set but missing is a problem.
    /// </summary>
    private static ActivateSettings? ReadActivateSettings(IConfiguration configuration, TextWriter error, out string problem)
    {
        var platform = ReadPlatformSettings(configuration, out problem);
        if (platform is null)
        {
            return null;
        }

        var release = ReadReleaseSettings(configuration, error, out problem);
        if (release is null)
        {
            return null;
        }

        return ReadRealmMail(configuration, out problem) is { } mail
            ? new ActivateSettings(platform, release.Packages, release.Modules, release.InlineRecomputeLimit, release.ActivationTimeout, mail)
            : null;
    }

    /// <summary>
    /// Reads the <c>Email</c> block that a realm's mail server falls back to, for the commands that write a realm. A problem names
    /// the setting and never its value, and one line holds each problem.
    /// </summary>
    internal static RealmMail? ReadRealmMail(IConfiguration configuration, out string problem)
    {
        var problems = new List<string>();
        var mail = RealmMail.Read(configuration, problems);
        problem = string.Join(Environment.NewLine, problems);
        return problems.Count == 0 ? mail : null;
    }

    // The part of the activate settings that needs no platform database: the catalogs, the inline recompute limit and the activation timeout.
    private static ReleaseSettings? ReadReleaseSettings(IConfiguration configuration, TextWriter error, out string problem)
    {
        problem = string.Empty;
        var limit = ReleaseActivator.DefaultInlineRecomputeLimit;
        var limitText = configuration[InlineRecomputeLimitSetting];
        if (!string.IsNullOrWhiteSpace(limitText)
            && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1))
        {
            problem = $"The setting {InlineRecomputeLimitSetting} must be a positive whole number.";
            return null;
        }

        TimeSpan activationTimeout;
        try
        {
            activationTimeout = ReleaseActivator.ReadActivationTimeout(configuration);
        }
        catch (InvalidOperationException)
        {
            problem = $"The setting {ReleaseActivator.ActivationTimeoutSetting} must be a duration of at least one millisecond, such as 00:30:00.";
            return null;
        }

        var packagesRoot = ResolveFolder(configuration[PackageCatalog.PathSetting]);
        var modulesRoot = ResolveFolder(configuration[ModuleCatalog.PathSetting]);
        if (packagesRoot is not null && !Directory.Exists(packagesRoot))
        {
            problem = $"The folder set by {PackageCatalog.PathSetting} does not exist.";
            return null;
        }

        if (modulesRoot is not null && !Directory.Exists(modulesRoot))
        {
            problem = $"The folder set by {ModuleCatalog.PathSetting} does not exist.";
            return null;
        }

        PackageCatalog packages;
        ModuleCatalog modules;
        try
        {
            packages = PackageCatalog.Load(packagesRoot);
            modules = ModuleCatalog.Load(modulesRoot);
        }
        catch (InvalidOperationException)
        {
            // The catalogs' own messages name the folder, and a settings message never holds a value.
            problem = $"The folder set by {PackageCatalog.PathSetting} or {ModuleCatalog.PathSetting} cannot be listed.";
            return null;
        }

        foreach (var diagnostic in packages.Diagnostics.Concat(modules.Diagnostics))
        {
            error.WriteLine($"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}");
        }

        return new ReleaseSettings(packages, modules, limit, activationTimeout);
    }

    private static string? ResolveFolder(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ? null : Path.GetFullPath(setting);

    private static MigrateRequest? ParseMigrate(string[] args)
    {
        string? tenant = null;
        int? parallel = null;
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            switch (args[i])
            {
                case "--tenant" when tenant is null:
                    tenant = args[i + 1];
                    break;
                case "--parallel" when parallel is null && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value):
                    parallel = value;
                    break;
                default:
                    return null;
            }
        }

        return new MigrateRequest(tenant, parallel ?? 1);
    }

    /// <summary><c>tenant backup</c> needs the platform database and the installation key, and <c>PATH</c> to find <c>pg_dump</c>. It needs no admin role and no client secret.</summary>
    private static BackupSettings? ReadBackupSettings(IConfiguration configuration, out string problem)
    {
        var platform = ReadPlatformSettings(configuration, out problem);
        if (platform is null)
        {
            return null;
        }

        var path = configuration[PathSetting];
        if (string.IsNullOrWhiteSpace(path))
        {
            problem = $"The environment variable {PathSetting} is required, to find pg_dump.";
            return null;
        }

        return new BackupSettings(platform.PlatformConnectionString, platform.Cipher, path);
    }

    /// <summary>
    /// <c>tenant restore</c> reads the settings of <c>tenant create</c>, and <c>PATH</c> to find <c>pg_restore</c>. It reads
    /// <c>Extensions:Path</c> when the archive requires a package. It never reads <c>Modules:Path</c>, because a missing module refuses nothing.
    /// The identity provider settings are optional here: only an archive of a tenant with its own realm needs them, which the check step finds out.
    /// A value that is given must still be valid.
    /// </summary>
    private static RestoreSettings? ReadRestoreSettings(IConfiguration configuration, out string problem)
    {
        var create = ReadCreateSettings(configuration, readClientSecret: true, out problem);
        if (create is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(configuration[IdentityProviderUrlSetting]))
        {
            var environment = new AdminEnvironment(configuration[DotnetEnvironmentSetting] ?? Environments.Production);
            var identityProvider = ReadIdentityProvider(configuration, environment, out problem);
            if (identityProvider is null)
            {
                return null;
            }

            if (ReadRealmMail(configuration, out problem) is not { } mail)
            {
                return null;
            }

            create = create with { IdentityProvider = identityProvider, Mail = mail };
        }

        var path = configuration[PathSetting];
        if (string.IsNullOrWhiteSpace(path))
        {
            problem = $"The environment variable {PathSetting} is required, to find {TenantRestore.PgRestore}.";
            return null;
        }

        return new RestoreSettings(create, path, configuration[PackageCatalog.PathSetting]);
    }

    /// <summary><c>tenant suspend</c>, <c>resume</c> and <c>list</c> need only the platform database. They decrypt nothing and create no role or database.</summary>
    internal static string? ReadDirectorySettings(IConfiguration configuration, out string problem)
    {
        problem = "";
        var platform = configuration[PlatformSetting];
        if (string.IsNullOrWhiteSpace(platform))
        {
            problem = $"The setting {PlatformSetting} is required.";
            return null;
        }

        try
        {
            return new NpgsqlConnectionStringBuilder(platform) { Pooling = false }.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            problem = $"The setting {PlatformSetting} is not a valid connection string.";
            return null;
        }
    }

    /// <summary>Reads the settings that <c>tenant create</c>, <c>tenant backup</c> and <c>migrate</c> need: the platform database and the key of the tenant secrets.</summary>
    internal static PlatformSettings? ReadPlatformSettings(IConfiguration configuration, out string problem)
    {
        problem = "";
        var platform = configuration[PlatformSetting];
        var key = configuration[TenantSecretCipher.KeySetting];

        if (string.IsNullOrWhiteSpace(platform))
        {
            problem = $"The setting {PlatformSetting} is required.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            problem = $"The setting {TenantSecretCipher.KeySetting} is required.";
            return null;
        }

        string platformConnection;
        try
        {
            platformConnection = new NpgsqlConnectionStringBuilder(platform) { Pooling = false }.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            problem = $"The setting {PlatformSetting} is not a valid connection string.";
            return null;
        }

        TenantSecretCipher cipher;
        try
        {
            cipher = TenantSecretCipher.Load(configuration);
        }
        catch (InvalidOperationException exception)
        {
            // The cipher's message names the setting and never its value.
            problem = exception.Message;
            return null;
        }

        return new PlatformSettings(platformConnection, cipher);
    }

    /// <summary>
    /// Reads the settings of <c>tenant create</c>: the common ones, the admin role and, when <paramref name="readClientSecret"/> is true,
    /// the OIDC client secret, which <c>tenant restore</c> reads and requires only when it keeps the archive's sign-in.
    /// A command that does not read it never does, even when it is set.
    /// </summary>
    private static CreateSettings? ReadCreateSettings(IConfiguration configuration, bool readClientSecret, out string problem)
    {
        problem = "";
        var admin = configuration[AdminSetting];
        var secret = readClientSecret ? configuration[ClientSecretSetting] : null;

        if (string.IsNullOrWhiteSpace(admin))
        {
            problem = $"The setting {AdminSetting} is required.";
            return null;
        }

        var common = ReadPlatformSettings(configuration, out problem);
        if (common is null)
        {
            return null;
        }

        NpgsqlConnectionStringBuilder adminBuilder;
        try
        {
            adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Pooling = false };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            problem = $"The setting {AdminSetting} is not a valid connection string.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(adminBuilder.Host))
        {
            problem = $"The setting {AdminSetting} names no host.";
            return null;
        }

        return new CreateSettings(adminBuilder, common.PlatformConnectionString, common.Cipher, secret);
    }

    /// <summary>
    /// Reads the identity provider settings of <c>tenant create</c>. The address must be an absolute http or https URL, and https
    /// outside Development and Testing. A message names the setting and never its value.
    /// </summary>
    private static IdentityProviderAdmin? ReadIdentityProvider(IConfiguration configuration, IHostEnvironment environment, out string problem)
    {
        problem = "";
        var url = configuration[IdentityProviderUrlSetting];
        var clientId = configuration[IdentityProviderAdminClientIdSetting];
        var clientSecret = configuration[IdentityProviderAdminClientSecretSetting];

        if (string.IsNullOrWhiteSpace(url))
        {
            problem = $"The setting {IdentityProviderUrlSetting} is required.";
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            problem = $"The setting {IdentityProviderUrlSetting} must be an absolute http or https URL.";
            return null;
        }

        if (address.Scheme != Uri.UriSchemeHttps && DatabaseTlsRequirement.Applies(environment))
        {
            problem = $"The setting {IdentityProviderUrlSetting} must be an https URL.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            problem = $"The setting {IdentityProviderAdminClientIdSetting} is required.";
            return null;
        }

        if (string.IsNullOrEmpty(clientSecret))
        {
            problem = $"The setting {IdentityProviderAdminClientSecretSetting} is required.";
            return null;
        }

        // A trailing slash keeps a path of the address when a relative address is added to it.
        var baseAddress = new UriBuilder(address) { Query = "", Fragment = "" }.Uri;
        if (!baseAddress.AbsolutePath.EndsWith('/'))
        {
            baseAddress = new Uri(baseAddress.ToString() + "/");
        }

        return new IdentityProviderAdmin(baseAddress, clientId, clientSecret);
    }

    /// <summary>Returns the reason to refuse the command, or null. It checks the admin role and the input before any tenant work.</summary>
    private static async Task<string?> ValidateAsync(CreateSettings settings, TenantCreateRequest request, CancellationToken cancellationToken)
    {
        var problem = await AdminRoleProblemAsync(settings, cancellationToken) ?? IdAndHostsProblem(request.Id, request.Hosts);
        if (problem is not null)
        {
            return problem;
        }

        if (request.PreviewOf is not null)
        {
            return BindingsProblem(request.Bindings)
                ?? await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken)
                ?? await PreviewProblemAsync(settings.PlatformConnectionString, request, cancellationToken);
        }

        // The realm is named after the tenant, and the master realm is the identity provider's own admin realm.
        if (request.Id == "master")
        {
            return "The tenant id 'master' is reserved by the identity provider.";
        }

        return await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);
    }

    // A message names the binding and never its value.
    private static string? BindingsProblem(IReadOnlyList<KeyValuePair<string, string>> bindings)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in bindings)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A binding name must not be blank.";
            }

            if (!names.Add(name))
            {
                return $"The binding '{name}' is given more than once. Binding names are compared ignoring letter case.";
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return $"The binding '{name}' must be an absolute http or https URL.";
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the reason that the tenant cannot be a preview tenant of its main tenant, or null. A main tenant is known, is no
    /// preview tenant and has no other preview tenant. A message names tenant ids only.
    /// </summary>
    private static async Task<string?> PreviewProblemAsync(string platformConnectionString, TenantCreateRequest request, CancellationToken cancellationToken)
    {
        var main = request.PreviewOf!;
        if (main == request.Id)
        {
            return $"The tenant '{request.Id}' cannot be a preview tenant of itself.";
        }

        try
        {
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
            var mainRow = await platform.Tenants.AsNoTracking()
                .Where(tenant => tenant.Id == main)
                .Select(tenant => new { tenant.PreviewOf })
                .FirstOrDefaultAsync(cancellationToken);
            if (mainRow is null)
            {
                return $"The main tenant '{main}' does not exist.";
            }

            if (mainRow.PreviewOf is not null)
            {
                return $"The main tenant '{main}' is itself a preview tenant.";
            }

            var other = await platform.Tenants.AsNoTracking()
                .Where(tenant => tenant.PreviewOf == main && tenant.Id != request.Id)
                .Select(tenant => tenant.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (other is not null)
            {
                return $"The main tenant '{main}' already has the preview tenant '{other}'.";
            }
        }
        catch (PostgresException exception)
        {
            return $"The setting {PlatformSetting} could not be used: {exception.MessageText}";
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            return $"The setting {PlatformSetting} could not be used to connect to the platform database.";
        }

        return null;
    }

    /// <summary>The checks of <see cref="ValidateAsync"/> that do not depend on the OIDC options: the OIDC settings of a restore come from its archive.</summary>
    private static async Task<string?> ValidateRestoreAsync(CreateSettings settings, TenantRestoreRequest request, CancellationToken cancellationToken) =>
        await AdminRoleProblemAsync(settings, cancellationToken)
        ?? IdAndHostsProblem(request.Id, request.Hosts)
        ?? await PlatformTablesProblemAsync(settings.PlatformConnectionString, cancellationToken);

    private static async Task<string?> AdminRoleProblemAsync(CreateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await using var admin = new NpgsqlConnection(settings.Admin.ConnectionString);
            await admin.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT EXISTS (SELECT 1 FROM pg_roles s WHERE s.rolsuper AND pg_has_role(current_user, s.oid, 'MEMBER')),
                       (SELECT rolcreatedb AND rolcreaterole FROM pg_roles WHERE rolname = current_user)
                """,
                admin);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetBoolean(0))
            {
                return $"The role of {AdminSetting} is a superuser or a member of one. Use a role with CREATEDB and CREATEROLE that is not.";
            }

            if (reader.IsDBNull(1) || !reader.GetBoolean(1))
            {
                return $"The role of {AdminSetting} must have CREATEDB and CREATEROLE.";
            }
        }
        catch (PostgresException exception)
        {
            return $"The setting {AdminSetting} could not be used: {exception.MessageText}";
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            return $"The setting {AdminSetting} could not be used to connect to the database server.";
        }

        return null;
    }

    private static string? IdAndHostsProblem(string id, IReadOnlyList<string> hosts)
    {
        if (!IdPattern().IsMatch(id))
        {
            return "The tenant id must have 1 to 50 lowercase letters, digits and hyphens.";
        }

        return hosts.Any(string.IsNullOrWhiteSpace) ? "A host must not be blank." : null;
    }

    // The first existing file of that name in the directories of the setting, as a shell would find it.
    internal static string? FindTool(string searchPath, string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, file);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static async Task<bool> PlatformTablesExistAsync(string platformConnectionString, CancellationToken cancellationToken)
    {
        await using var platform = new NpgsqlConnection(platformConnectionString);
        await platform.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('platform.tenants') IS NOT NULL", platform);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Returns the reason that the platform database cannot be used, or null when its tables exist. The admin tool does not create them.</summary>
    public static async Task<string?> PlatformTablesProblemAsync(string platformConnectionString, CancellationToken cancellationToken)
    {
        try
        {
            if (!await PlatformTablesExistAsync(platformConnectionString, cancellationToken))
            {
                return "The platform tables do not exist. Start the server once under Tenancy:Source=directory, which creates them.";
            }
        }
        catch (PostgresException exception)
        {
            return $"The setting {PlatformSetting} could not be used: {exception.MessageText}";
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            return $"The setting {PlatformSetting} could not be used to connect to the platform database.";
        }

        return null;
    }

    [GeneratedRegex(@"^[a-z0-9-]{1,50}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}

/// <summary>
/// <c>tenant create</c>. A main tenant has no <paramref name="PreviewOf"/> and gets a realm. A preview tenant has
/// <paramref name="PreviewOf"/>, no realm and any <paramref name="Bindings"/>. A main tenant may have an
/// <paramref name="Administrator"/>, the normalised email of the person to invite as its first administrator.
/// </summary>
internal sealed record TenantCreateRequest(
    string Id,
    IReadOnlyList<string> Hosts,
    string? PreviewOf,
    IReadOnlyList<KeyValuePair<string, string>> Bindings,
    string? Administrator = null);

/// <summary><c>tenant oidc set</c>. <paramref name="WithClientKey"/> says that the client key is to be read, and <paramref name="Change"/> does not hold it yet.</summary>
internal sealed record OidcSetRequest(string Id, bool WithClientKey, TenantSignInChange Change);

internal sealed record MigrateRequest(string? Tenant, int Parallel);

internal sealed record ActivateRequest(string Folder, string? Tenant);

internal sealed record TenantBackupRequest(string Id, string Out, string BackupKeyFile);

internal sealed record TenantRestoreRequest(string File, string Id, IReadOnlyList<string> Hosts, string BackupKeyFile);

/// <summary>The settings every command reads: the platform database and the key of the tenant secrets.</summary>
internal sealed record PlatformSettings(string PlatformConnectionString, TenantSecretCipher Cipher);

/// <summary>The settings of <c>activate</c>: the common ones, the loaded packages and modules, the inline recompute limit, the activation timeout and the realm's mail fallback.</summary>
internal sealed record ActivateSettings(PlatformSettings Platform, PackageCatalog Packages, ModuleCatalog Modules, int InlineRecomputeLimit, TimeSpan ActivationTimeout, RealmMail Mail);

/// <summary>The settings of the release steps that need no platform database: the loaded packages and modules, the inline recompute limit and the activation timeout.</summary>
internal sealed record ReleaseSettings(PackageCatalog Packages, ModuleCatalog Modules, int InlineRecomputeLimit, TimeSpan ActivationTimeout);

/// <summary>The host environment that the tenant configuration check reads, from <c>DOTNET_ENVIRONMENT</c>. This host has no other.</summary>
internal sealed class AdminEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Axis.Admin";

    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed record BackupSettings(string PlatformConnectionString, TenantSecretCipher Cipher, string SearchPath);

/// <summary>
/// The settings of <c>tenant create</c>, which also needs the admin role. <paramref name="OidcClientSecret"/> is for <c>tenant restore</c>.
/// <paramref name="IdentityProvider"/> and <paramref name="Mail"/> are set only by <c>tenant create</c> of a main tenant, and by <c>tenant restore</c> when it has the identity provider settings.
/// </summary>
internal sealed record CreateSettings(
    NpgsqlConnectionStringBuilder Admin,
    string PlatformConnectionString,
    TenantSecretCipher Cipher,
    string? OidcClientSecret,
    IdentityProviderAdmin? IdentityProvider = null,
    RealmMail? Mail = null);

/// <summary>The settings of <c>tenant restore</c>: those of <c>tenant create</c>, the <c>PATH</c> and the installed extension packages, which may be unset.</summary>
internal sealed record RestoreSettings(CreateSettings Create, string SearchPath, string? ExtensionsPath);
