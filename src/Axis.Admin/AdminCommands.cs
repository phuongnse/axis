using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Data;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The commands of the admin host. Exit code 0 means the command succeeded, 1 that a step failed and 2 that nothing ran:
/// a usage or settings error. A message that names a setting never holds its value.
/// </summary>
internal static partial class AdminCommands
{
    public const string AdminSetting = "Admin:ConnectionString";

    public const string PlatformSetting = "ConnectionStrings:PlatformSchema";

    public const string InlineRecomputeLimitSetting = "Activation:InlineRecomputeLimit";

    public const string ScenarioServerSetting = "Scenarios:ServerConnectionString";

    public const string DotnetEnvironmentSetting = "DOTNET_ENVIRONMENT";

    private const string Provisioning = "provisioning";

    private const string ActiveStatus = "active";

    public const string ClientSecretSetting = "AXIS_OIDC_CLIENT_SECRET";

    public const string PathSetting = "PATH";

    private const string Usage =
        """
        Usage: tenant create <id> --host <host>... --oidc-authority <url> --oidc-client-id <id>
               tenant create <id> --preview-of <main> --host <host>... [--binding <name>=<value>]...
               tenant backup <id> --out <file>
               tenant restore <file> --tenant <id> --host <host>...
               migrate [--tenant <id>] [--parallel <n>]
               activate <folder> [--tenant <id>]
               tenant suspend <id>
               tenant resume <id>
               tenant list
               publisher
               version
        """;

    public static Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(args, configuration, output, error, Publisher.DefaultLeaseDuration, Publisher.DefaultTestLimit, cancellationToken);

    // The publisher lease and the test limit are parameters so that tests can use short ones. The product has no setting for them.
    internal static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan publisherLease,
        TimeSpan publisherTestLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length > 0 && args[0] == "migrate")
        {
            return await MigrateAsync(args, configuration, output, error, cancellationToken);
        }

        if (args.Length > 0 && args[0] == "activate")
        {
            return await ActivateAsync(args, configuration, output, error, cancellationToken);
        }

        if (args.Length > 0 && args[0] == "publisher")
        {
            return await PublisherAsync(args, configuration, output, error, publisherLease, publisherTestLimit, cancellationToken);
        }

        if (args is ["version"])
        {
            output.WriteLine(typeof(AdminCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return 0;
        }

        if (args.Length >= 2 && args[0] == "tenant" && args[1] == "backup")
        {
            return await BackupAsync(args, configuration, output, error, cancellationToken);
        }

        if (args.Length >= 2 && args[0] == "tenant" && args[1] == "restore")
        {
            return await RestoreAsync(args, configuration, output, error, cancellationToken);
        }

        if (args.Length >= 2 && args[0] == "tenant" && args[1] is "suspend" or "resume" or "list")
        {
            return await StatusAsync(args, configuration, output, error, cancellationToken);
        }

        var request = ParseTenantCreate(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadCreateSettings(configuration, needsClientSecret: request.PreviewOf is null, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
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

        return await new TenantCreate(settings, request, output, cancellationToken).RunAsync();
    }

    private static async Task<int> StatusAsync(
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
            "suspend" => await TenantStatus.SuspendAsync(connectionString, args[2], output, error, cancellationToken),
            "resume" => await TenantStatus.ResumeAsync(connectionString, args[2], output, error, cancellationToken),
            _ => await TenantStatus.ListAsync(connectionString, output, error, cancellationToken),
        };
    }

    private static async Task<int> BackupAsync(
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

        BackupPlan? plan;
        try
        {
            plan = await TenantBackup.PrepareAsync(settings, request, error, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return plan is null ? 2 : await new TenantBackup(settings, plan, output, cancellationToken).RunAsync();
    }

    private static async Task<int> RestoreAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
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

        var pgRestore = FindTool(settings.SearchPath, TenantRestore.PgRestore);
        if (pgRestore is null)
        {
            error.WriteLine($"{TenantRestore.PgRestore} was not found in the directories of {PathSetting}.");
            return 2;
        }

        RestorePlan? plan;
        try
        {
            var refusal = await ValidateRestoreAsync(settings.Create, request, cancellationToken);
            if (refusal is not null)
            {
                error.WriteLine(refusal);
                return 2;
            }

            plan = await TenantRestore.PrepareAsync(settings, request, pgRestore, output, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("The command was canceled before any tenant work.");
            return 1;
        }

        return plan is null ? 2 : await new TenantRestore(settings.Create, plan, output, cancellationToken).RunAsync();
    }

    private static TenantRestoreRequest? ParseTenantRestore(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var hosts = new List<string>();
        string? id = null;
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
                default:
                    return null;
            }
        }

        return hosts.Count == 0 || id is null ? null : new TenantRestoreRequest(args[2], id, hosts);
    }

    private static TenantBackupRequest? ParseTenantBackup(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        string? file = null;
        for (var i = 3; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] != "--out" || file is not null || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return null;
            }

            file = args[i + 1];
        }

        return file is null ? null : new TenantBackupRequest(args[2], file);
    }

    private static TenantCreateRequest? ParseTenantCreate(string[] args)
    {
        if (args.Length < 3 || args[0] != "tenant" || args[1] != "create" || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var hosts = new List<string>();
        var bindings = new List<KeyValuePair<string, string>>();
        string? authority = null;
        string? clientId = null;
        string? previewOf = null;
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
                case "--oidc-authority" when authority is null:
                    authority = value;
                    break;
                case "--oidc-client-id" when clientId is null:
                    clientId = value;
                    break;
                case "--preview-of" when previewOf is null:
                    previewOf = value;
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

        // A preview tenant takes the sign-in settings of its main tenant, so it takes no OIDC option. A binding is only for a preview.
        if (previewOf is not null)
        {
            return authority is null && clientId is null
                ? new TenantCreateRequest(args[2], hosts, null, null, previewOf, bindings)
                : null;
        }

        return authority is null || clientId is null || bindings.Count > 0
            ? null
            : new TenantCreateRequest(args[2], hosts, authority, clientId, null, bindings);
    }

    private static async Task<int> MigrateAsync(
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

            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
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

        return await new TenantMigrate(settings, tenants, request.Parallel, output, cancellationToken).RunAsync();
    }

    private static async Task<int> ActivateAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
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

        return await new TenantActivate(
            settings.Platform, tenants, bindings, folder, settings.Packages, settings.Modules, settings.InlineRecomputeLimit, output, error, cancellationToken).RunAsync();
    }

    private static async Task<int> PublisherAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan lease,
        TimeSpan testLimit,
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
            ? await DirectoryPublisherAsync(configuration, output, error, lease, testLimit, cancellationToken)
            : await ConfigurationPublisherAsync(configuration, output, error, lease, testLimit, cancellationToken);
    }

    private static async Task<int> DirectoryPublisherAsync(
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        TimeSpan lease,
        TimeSpan testLimit,
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

        return await new Publisher(
            new ReleaseSettings(settings.Packages, settings.Modules, settings.InlineRecomputeLimit),
            scenarioServer,
            new DirectoryPublisherTenants(settings.Platform, refreshInterval),
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

        var allowHttp = (configuration[DotnetEnvironmentSetting] ?? Environments.Production) == Environments.Development;
        return await new Publisher(
            settings,
            scenarioServer,
            new ConfigurationPublisherTenants(catalog.Tenants, allowHttp, output),
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
            bindings[id] = new ActivationBindings(id, named, AllowHttp: false, IsPreview: previewIds.Contains(id));
        }

        return bindings;
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
        return release is null ? null : new ActivateSettings(platform, release.Packages, release.Modules, release.InlineRecomputeLimit);
    }

    // The part of the activate settings that needs no platform database: the catalogs and the inline recompute limit.
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

        return new ReleaseSettings(packages, modules, limit);
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
    /// </summary>
    private static RestoreSettings? ReadRestoreSettings(IConfiguration configuration, out string problem)
    {
        var create = ReadCreateSettings(configuration, needsClientSecret: true, out problem);
        if (create is null)
        {
            return null;
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
    private static string? ReadDirectorySettings(IConfiguration configuration, out string problem)
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
    private static PlatformSettings? ReadPlatformSettings(IConfiguration configuration, out string problem)
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
        catch (InvalidOperationException)
        {
            // The cipher's own message mentions Tenancy:Source, which does not apply here.
            problem = $"The setting {TenantSecretCipher.KeySetting} must be {TenantSecretCipher.KeySize} bytes in base64.";
            return null;
        }

        return new PlatformSettings(platformConnection, cipher);
    }

    /// <summary>
    /// Reads the settings of <c>tenant create</c>: the common ones, the admin role and, unless it makes a preview tenant, the
    /// OIDC client secret. A preview tenant never reads the secret, even when it is set.
    /// </summary>
    private static CreateSettings? ReadCreateSettings(IConfiguration configuration, bool needsClientSecret, out string problem)
    {
        problem = "";
        var admin = configuration[AdminSetting];
        var secret = needsClientSecret ? configuration[ClientSecretSetting] : null;

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

        if (needsClientSecret && string.IsNullOrEmpty(secret))
        {
            problem = $"The environment variable {ClientSecretSetting} is required.";
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

        if (!Uri.TryCreate(request.OidcAuthority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps))
        {
            return "--oidc-authority must be an absolute http or https URL.";
        }

        if (string.IsNullOrWhiteSpace(request.OidcClientId))
        {
            return "--oidc-client-id must not be blank.";
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
/// <c>tenant create</c>. A main tenant has the OIDC settings and no <paramref name="PreviewOf"/>. A preview tenant has
/// <paramref name="PreviewOf"/>, no OIDC settings and any <paramref name="Bindings"/>.
/// </summary>
internal sealed record TenantCreateRequest(
    string Id,
    IReadOnlyList<string> Hosts,
    string? OidcAuthority,
    string? OidcClientId,
    string? PreviewOf,
    IReadOnlyList<KeyValuePair<string, string>> Bindings);

internal sealed record MigrateRequest(string? Tenant, int Parallel);

internal sealed record ActivateRequest(string Folder, string? Tenant);

internal sealed record TenantBackupRequest(string Id, string Out);

internal sealed record TenantRestoreRequest(string File, string Id, IReadOnlyList<string> Hosts);

/// <summary>The settings every command reads: the platform database and the key of the tenant secrets.</summary>
internal sealed record PlatformSettings(string PlatformConnectionString, TenantSecretCipher Cipher);

/// <summary>The settings of <c>activate</c>: the common ones, the loaded packages and modules and the inline recompute limit.</summary>
internal sealed record ActivateSettings(PlatformSettings Platform, PackageCatalog Packages, ModuleCatalog Modules, int InlineRecomputeLimit);

/// <summary>The settings of the release steps that need no platform database: the loaded packages and modules and the inline recompute limit.</summary>
internal sealed record ReleaseSettings(PackageCatalog Packages, ModuleCatalog Modules, int InlineRecomputeLimit);

/// <summary>The host environment that the tenant configuration check reads, from <c>DOTNET_ENVIRONMENT</c>. This host has no other.</summary>
internal sealed class AdminEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Axis.Admin";

    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed record BackupSettings(string PlatformConnectionString, TenantSecretCipher Cipher, string SearchPath);

/// <summary>The settings of <c>tenant create</c>, which also needs the admin role and, except for a preview tenant, the OIDC client secret.</summary>
internal sealed record CreateSettings(
    NpgsqlConnectionStringBuilder Admin,
    string PlatformConnectionString,
    TenantSecretCipher Cipher,
    string? OidcClientSecret);

/// <summary>The settings of <c>tenant restore</c>: those of <c>tenant create</c>, the <c>PATH</c> and the installed extension packages, which may be unset.</summary>
internal sealed record RestoreSettings(CreateSettings Create, string SearchPath, string? ExtensionsPath);
