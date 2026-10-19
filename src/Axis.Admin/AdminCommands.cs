using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    private const string Provisioning = "provisioning";

    public const string ClientSecretSetting = "AXIS_OIDC_CLIENT_SECRET";

    private const string Usage =
        "Usage: tenant create <id> --host <host>... --oidc-authority <url> --oidc-client-id <id>\n"
        + "       migrate [--tenant <id>] [--parallel <n>]";

    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
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

        var request = ParseTenantCreate(args);
        if (request is null)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var settings = ReadCreateSettings(configuration, out var problem);
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

    private static TenantCreateRequest? ParseTenantCreate(string[] args)
    {
        if (args.Length < 3 || args[0] != "tenant" || args[1] != "create" || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var hosts = new List<string>();
        string? authority = null;
        string? clientId = null;
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
                default:
                    return null;
            }
        }

        return hosts.Count == 0 || authority is null || clientId is null
            ? null
            : new TenantCreateRequest(args[2], hosts, authority, clientId);
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

        var settings = ReadSettings(configuration, out var problem);
        if (settings is null)
        {
            error.WriteLine(problem);
            return 2;
        }

        IReadOnlyList<TenantRow> tenants;
        try
        {
            if (!await PlatformTablesExistAsync(settings, cancellationToken))
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

    /// <summary>Reads the settings that every command needs: the platform database and the key of the tenant secrets.</summary>
    private static AdminSettings? ReadSettings(IConfiguration configuration, out string problem)
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

        return new AdminSettings(platformConnection, cipher);
    }

    /// <summary>Reads the settings of <c>tenant create</c>: the common ones, the admin role and the OIDC client secret.</summary>
    private static CreateSettings? ReadCreateSettings(IConfiguration configuration, out string problem)
    {
        problem = "";
        var admin = configuration[AdminSetting];
        var secret = configuration[ClientSecretSetting];

        if (string.IsNullOrWhiteSpace(admin))
        {
            problem = $"The setting {AdminSetting} is required.";
            return null;
        }

        var common = ReadSettings(configuration, out problem);
        if (common is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(secret))
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

    private static async Task<bool> PlatformTablesExistAsync(AdminSettings settings, CancellationToken cancellationToken)
    {
        await using var platform = new NpgsqlConnection(settings.PlatformConnectionString);
        await platform.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('platform.tenants') IS NOT NULL", platform);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Returns the reason to refuse the command, or null. It checks the admin role and the input before any tenant work.</summary>
    private static async Task<string?> ValidateAsync(CreateSettings settings, TenantCreateRequest request, CancellationToken cancellationToken)
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

        if (!IdPattern().IsMatch(request.Id))
        {
            return "The tenant id must have 1 to 50 lowercase letters, digits and hyphens.";
        }

        if (request.Hosts.Any(string.IsNullOrWhiteSpace))
        {
            return "A host must not be blank.";
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

        try
        {
            if (!await PlatformTablesExistAsync(settings, cancellationToken))
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

internal sealed record TenantCreateRequest(string Id, IReadOnlyList<string> Hosts, string OidcAuthority, string OidcClientId);

internal sealed record MigrateRequest(string? Tenant, int Parallel);

/// <summary>The settings every command reads: the platform database and the key of the tenant secrets.</summary>
internal record AdminSettings(string PlatformConnectionString, TenantSecretCipher Cipher);

/// <summary>The settings of <c>tenant create</c>, which also needs the admin role and the OIDC client secret.</summary>
internal sealed record CreateSettings(
    NpgsqlConnectionStringBuilder Admin,
    string PlatformConnectionString,
    TenantSecretCipher Cipher,
    string OidcClientSecret) : AdminSettings(PlatformConnectionString, Cipher);
