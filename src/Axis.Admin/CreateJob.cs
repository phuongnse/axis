using System.Text.Json;
using Axis.Configuration.Templates;
using Axis.Data;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// A part of the create job's settings: its value, or the problem that names the missing setting. A publisher that lacks a part still
/// runs publish requests, and only the create step that needs the part refuses.
/// </summary>
internal readonly record struct JobSetting<T>(T? Value, string? Problem)
    where T : class
{
    public static JobSetting<T> Of(T value) => new(value, null);

    public static JobSetting<T> Missing(string problem) => new(null, problem);
}

/// <summary>
/// The settings that the steps of the create job read, each as a <see cref="JobSetting{T}"/>. <paramref name="Create"/> holds the
/// admin connection, the identity provider, the mail fallback and the cell's backup key, with the first problem of those parts.
/// <paramref name="Activate"/> is what the publisher reads anyway.
/// </summary>
internal sealed record CreateJobSettings(
    JobSetting<CreateSettings> Create,
    JobSetting<string> ApplicationsPath,
    JobSetting<TemplateCatalog> Templates,
    JobSetting<string> TenantDomain,
    ActivateSettings Activate);

/// <summary>
/// The <c>create</c> job of the publisher: <c>tenant</c>, <c>application</c>, <c>activate</c>, <c>invite</c> and <c>resume</c>, which carry
/// out creating a tenant with the code of <c>tenant create</c>, <c>activate</c> and Studio's create from a template. The tenant stays
/// <c>provisioning</c>, so it is never served, until <c>resume</c> sets it <c>active</c>. The <c>tenant</c> step writes the quota rows of the input, if it has any. Each step can run again and reuses what an earlier run
/// did. Before its own work a step returns the problem of any setting it needs that is missing, and a missing or unreadable input as a refusal.
/// </summary>
internal static class CreateJob
{
    private const string Command = "tenant create";

    private const string NoInput = "The job has no input.";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<JobStep> Steps(CreateJobSettings settings, HttpClient http, TextWriter output, TextWriter error) =>
    [
        new JobStep("tenant", (_, id, input, ct) => TenantAsync(settings, http, output, id, input, ct)),
        new JobStep("application", (_, id, input, ct) => ApplicationAsync(settings, id, input, ct)),
        new JobStep("activate", (_, id, input, ct) => ActivateAsync(settings, http, output, error, id, input, ct)),
        new JobStep("invite", (_, id, input, ct) => InviteAsync(settings, http, id, input, ct)),
        new JobStep("resume", (_, id, _, ct) => ResumeAsync(settings, id, ct)),
    ];

    private static async Task<string?> TenantAsync(CreateJobSettings settings, HttpClient http, TextWriter output, string id, string? input, CancellationToken cancellationToken)
    {
        if ((settings.Create.Problem ?? settings.TenantDomain.Problem) is { } problem)
        {
            return problem;
        }

        if (ReadInput(input) is not { } read)
        {
            return NoInput;
        }

        var create = settings.Create.Value!;
        var request = new TenantCreateRequest(id, [$"{id}.{settings.TenantDomain.Value}"], null, [], null, StayProvisioning: true, Quotas: read.Quotas);
        if (await AdminCommands.ValidateAsync(create, request, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        // The job writes its own audit record when it ends, so the command writes none.
        await using var tenant = new TenantCreate(create, request, new TenantRealm(http, create.IdentityProvider!), http, audit: null, Command, output, cancellationToken);
        if (await tenant.RunAsync() == 0)
        {
            return null;
        }

        // A stop is no failure of the tenant, and a transient error, such as an identity provider that is down, gets the next claim.
        cancellationToken.ThrowIfCancellationRequested();
        return tenant.Refusal ?? throw new InvalidOperationException(tenant.Failure ?? "The tenant could not be created.");
    }

    private static async Task<string?> ApplicationAsync(CreateJobSettings settings, string id, string? input, CancellationToken cancellationToken)
    {
        if ((settings.ApplicationsPath.Problem ?? settings.Templates.Problem) is { } problem)
        {
            return problem;
        }

        if (Plan(settings, id, input, out var template, out var name, out var target) is { } refusal)
        {
            return refusal;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (AlreadyThere(target, name))
        {
            return null;
        }

        if (Directory.Exists(target))
        {
            return $"The Studio folder of the tenant already has a folder named '{Path.GetFileName(target)}' that is not this application.";
        }

        // The copy is written to a hidden folder and then renamed, so a reader never sees half an application.
        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}-{Guid.NewGuid():N}");
        var prefix = temporary + Path.DirectorySeparatorChar;
        try
        {
            foreach (var (file, bytes) in TemplateApplications.FromTemplate(template, name))
            {
                var path = Path.GetFullPath(Path.Combine(temporary, file));
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A file of a new application is outside its folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }

            Directory.Move(temporary, target);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }

            throw;
        }

        return null;
    }

    private static async Task<string?> ActivateAsync(
        CreateJobSettings settings,
        HttpClient http,
        TextWriter output,
        TextWriter error,
        string id,
        string? input,
        CancellationToken cancellationToken)
    {
        if ((settings.ApplicationsPath.Problem ?? settings.Templates.Problem) is { } problem)
        {
            return problem;
        }

        if (Plan(settings, id, input, out _, out _, out var target) is { } refusal)
        {
            return refusal;
        }

        var activate = settings.Activate;
        TenantRow? row;
        Dictionary<string, ActivationBindings> bindings;
        Dictionary<string, IReadOnlyDictionary<string, TenantBinding>> realmBindings;
        Dictionary<string, RealmLegal> realmLegal;
        await using (var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(activate.Platform.PlatformConnectionString).Options))
        {
            row = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
            if (row is null)
            {
                return TenantStatus.UnknownMessage;
            }

            bindings = await AdminCommands.ReadBindingsAsync(platform, [id], cancellationToken);
            realmBindings = await AdminCommands.ReadRealmBindingsAsync(platform, [id], activate.Platform.Cipher, cancellationToken);
            realmLegal = await AdminCommands.ReadRealmLegalAsync(platform, [row], cancellationToken);
        }

        using var captured = new StringWriter();
        var exit = await new TenantActivate(
            activate.Platform,
            [row],
            bindings,
            realmBindings,
            realmLegal,
            activate.Mail,
            http,
            target,
            activate.Packages,
            activate.Modules,
            activate.InlineRecomputeLimit,
            activate.ActivationTimeout,
            audit: null,
            captured,
            error,
            cancellationToken).RunAsync();

        var lines = captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        if (exit == 0)
        {
            return null;
        }

        // A stop is no failed activation. A failed one is a refusal, because a rerun would fail the same way.
        cancellationToken.ThrowIfCancellationRequested();
        const string marker = " failed: ";
        var failed = lines.FirstOrDefault(line => line.Contains(marker, StringComparison.Ordinal));
        return failed is null ? "The application could not be activated." : failed[(failed.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
    }

    private static async Task<string?> InviteAsync(CreateJobSettings settings, HttpClient http, string id, string? input, CancellationToken cancellationToken)
    {
        if (ReadInput(input) is not { } read)
        {
            return NoInput;
        }

        var platform = settings.Activate.Platform;
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform.PlatformConnectionString).Options);
        var row = await context.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
        if (row is null)
        {
            return TenantStatus.UnknownMessage;
        }

        await using var steps = new TenantDatabaseSteps(
            platform.Cipher, row, row.DatabaseHost, row.DatabasePort, TenantDatabaseTls.From(platform.PlatformConnectionString), cancellationToken);
        await TenantAdministratorInvite.InviteAsync(http, row, platform.Cipher, steps, read.Administrator, Command, cancellationToken);
        return null;
    }

    private static async Task<string?> ResumeAsync(CreateJobSettings settings, string id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(settings.Activate.Platform.PlatformConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Unchanged means that the tenant already left provisioning, which is the wanted end.
        return await TenantStatus.ActivateChangeAsync(connection, id, cancellationToken) == StatusChange.Unknown ? TenantStatus.UnknownMessage : null;
    }

    private sealed record CreateInput(string Application, string Administrator, Dictionary<string, long?>? Quotas = null);

    private static CreateInput? ReadInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var input = JsonSerializer.Deserialize<CreateInput>(json, _json);
            return input is { Application.Length: > 0, Administrator.Length: > 0 } ? input : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Finds the template, the application's name and its folder in the tenant's Studio folder, or returns the refusal. Every step that needs
    // the folder recomputes it, so a rerun needs no stored state.
    private static string? Plan(CreateJobSettings settings, string id, string? input, out InstalledTemplate template, out string name, out string target)
    {
        template = null!;
        name = "";
        target = "";
        if (ReadInput(input) is not { } read)
        {
            return NoInput;
        }

        var found = settings.Templates.Value!.Newest.FirstOrDefault(candidate => string.Equals(candidate.Manifest.Id, read.Application, StringComparison.Ordinal));
        if (found is null)
        {
            return $"The application template '{read.Application}' is not installed.";
        }

        if (TemplateApplications.ApplicationName(found) is not { } applicationName)
        {
            return $"The application template '{read.Application}' has no application manifest.";
        }

        template = found;
        name = applicationName;
        target = Path.Combine(Path.TrimEndingDirectorySeparator(settings.ApplicationsPath.Value!), id, TemplateApplications.Kebab(name));
        return null;
    }

    // The target holds an application of this name, ignoring letter case, from an earlier run.
    private static bool AlreadyThere(string target, string name)
    {
        var manifest = Path.Combine(target, "application.json");
        if (!File.Exists(manifest))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("name", out var existing)
                && existing.ValueKind == JsonValueKind.String
                && string.Equals(existing.GetString(), name, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
