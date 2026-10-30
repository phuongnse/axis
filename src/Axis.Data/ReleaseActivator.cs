using System.Data.Common;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Realms;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data.Storage;

namespace Axis.Data;

/// <summary>
/// Makes a compiled release the active release of its application: it checks that no other
/// application is active under the manifest name or holds one of its site paths, checks that the
/// tenant has every deployment binding its connectors, its <c>oidc</c> identity providers and the package operations
/// its processes use name with the right kind, checks that no identity provider would make one person two users
/// of the tenant, provisions the
/// entity tables and then sets the active release with its site paths. Between the two it recomputes the stored
/// computed values of each entity whose computed definitions differ from the previously active release's.
/// <para>
/// On a preview tenant (<see cref="ActivationBindings.IsPreview"/>) a missing binding is a warning, which the result
/// carries while activation goes on. A binding of the wrong kind and an <c>http</c> URL outside Development are still
/// errors.
/// </para>
/// <para>
/// Activation reads the identity providers of the tenant's other active releases and plans the realm from them, the
/// release and the tenant's platform sign-in (<see cref="ActivationBindings.PlatformProviders"/>, <see cref="ActivationResult.Realm"/>),
/// so a provider that only the platform sign-in names stays in the realm. A provider that names the same issuer binding as another provider with
/// other client bindings, or the same bindings with other <c>claims</c>, is refused with
/// <see cref="DiagnosticCodes.IdentityProviderSplitsUser"/> before provisioning. <c>Axis.Admin</c> applies the plan to the realm after the activation commits.
/// </para>
/// <para>
/// It also refuses a module upgrade that drops a module entity or field, or changes a field's type, with
/// <see cref="DiagnosticCodes.NonAdditiveModuleUpgrade"/>, comparing the module copies with those of the active release.
/// </para>
/// <para>
/// Provisioning, the pending migrations and the active release write run in one transaction under
/// the schema lock, so the migration history check and the write see the same history: no other
/// activation can commit a rename or drop between them. A refused name or site path, or any failure,
/// rolls everything back and the previous release stays active. The store and
/// <see cref="DataDbContext"/> must share one open tenant connection, or the store throws before
/// anything commits.
/// </para>
/// <para>
/// The same write adds a row to the release history, unless the release is already the active one. The row
/// names the actor, which is <see cref="ActivationActor.Startup"/> unless the caller passes another.
/// </para>
/// <para>
/// When a migration is pending, the same transaction holds the application's <see cref="StartLock"/>
/// exclusively until it commits, so a process start either commits first or waits and pins the new release.
/// Under both locks, a destructive migration is refused with <see cref="DiagnosticCodes.OldReleaseWorkOpen"/>
/// while running or waiting process instances are pinned to another release that uses the column it changes.
/// </para>
/// </summary>
public static class ReleaseActivator
{
    /// <summary>The default most rows of one entity that activation recomputes inline.</summary>
    public const int DefaultInlineRecomputeLimit = 10_000;

    /// <summary>
    /// Activates the release in <paramref name="compiled"/> with the default inline recompute limit,
    /// <see cref="DefaultInlineRecomputeLimit"/> rows. See the overload with a limit.
    /// </summary>
    public static Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled,
        IActiveReleaseStore store,
        DataDbContext data,
        ActivationBindings bindings,
        IOpenInstanceCounter openInstances,
        CancellationToken cancellationToken = default) =>
        ActivateAsync(compiled, store, data, bindings, openInstances, DefaultInlineRecomputeLimit, cancellationToken);

    /// <summary>
    /// Activates the release in <paramref name="compiled"/> as the startup activation does. See the overload with an actor.
    /// </summary>
    public static Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled,
        IActiveReleaseStore store,
        DataDbContext data,
        ActivationBindings bindings,
        IOpenInstanceCounter openInstances,
        int inlineRecomputeLimit,
        CancellationToken cancellationToken = default) =>
        ActivateAsync(compiled, store, data, bindings, openInstances, inlineRecomputeLimit, ActivationActor.Startup, cancellationToken);

    /// <summary>
    /// Activates the release in <paramref name="compiled"/>, which must have a release and a model
    /// with the same application id, for the tenant whose deployment bindings are
    /// <paramref name="bindings"/>. <paramref name="openInstances"/> counts the process instances that are
    /// still open on the other releases of the application. The result holds the warnings of a successful activation,
    /// and the release is active when it holds no error. When a changed computed definition, or a migration
    /// <c>fill</c> or <c>convert</c>, affects more than <paramref name="inlineRecomputeLimit"/> rows of an entity,
    /// activation marks the entity pending and queues a <c>computed.backfill</c> work item instead of recomputing
    /// the rows. The release history records <paramref name="actor"/> for the activation. It also runs in a
    /// transaction that the caller enlisted <paramref name="data"/> and the store's context in. The
    /// caller then commits or rolls back the activation.
    /// </summary>
    public static async Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled,
        IActiveReleaseStore store,
        DataDbContext data,
        ActivationBindings bindings,
        IOpenInstanceCounter openInstances,
        int inlineRecomputeLimit,
        ActivationActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(openInstances);
        ArgumentNullException.ThrowIfNull(actor);
        if (compiled.Release is not { } release || compiled.Model is not { } model)
        {
            throw new ArgumentException("Only a compilation result with a release and a model can be activated.", nameof(compiled));
        }

        var manifest = model.Manifest;
        if (release.ApplicationId != manifest.Id)
        {
            throw new ArgumentException("The release and the model of the compilation result belong to different applications.", nameof(compiled));
        }

        var active = await store.FindByNameAsync(manifest.Name, cancellationToken);
        if (active is not null && active.ApplicationId != manifest.Id)
        {
            return new ActivationResult([NameActiveForOtherApplication(model)]);
        }

        // Every taken path is reported, so the author can fix them all at once.
        var takenPaths = new List<Diagnostic>();
        foreach (var site in model.Sites)
        {
            var holder = await store.FindBySitePathAsync(site.Path, cancellationToken);
            if (holder is not null && holder.ApplicationId != manifest.Id)
            {
                takenPaths.Add(SitePathActiveForOtherApplication(site));
            }
        }

        if (takenPaths.Count > 0)
        {
            return new ActivationResult(takenPaths);
        }

        // Checked before provisioning, so a refused release leaves no new tables behind.
        var bindingDiagnostics = CheckBindings(model, bindings);
        if (bindingDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ActivationResult(bindingDiagnostics);
        }

        // Also before provisioning: a provider that would split a person changes nothing.
        var realm = await PlanRealmAsync(store, model, bindings, cancellationToken);
        var splitting = realm.Conflicts
            .Where(conflict => conflict.ApplicationId == manifest.Id)
            .Select(SplitsUser)
            .ToList();
        if (splitting.Count > 0)
        {
            return new ActivationResult([.. bindingDiagnostics, .. splitting]);
        }

        var sitePaths = model.Sites.Select(site => site.Path).ToList();
        var migrations = new MigrationContext(
            release.Id,
            token => ReadStoredEntityIdsAsync(store, manifest.Id, token),
            (transaction, token) => ReadOldReleaseWorkAsync(store, openInstances, transaction, manifest.Id, release.Id, token),
            async (transaction, token) => ToDiagnostics(
                model,
                await store.TrySetInTransactionAsync(transaction, manifest.Id, manifest.Name, release.Id, sitePaths, DateTimeOffset.UtcNow, actor, token)))
        {
            ReadActiveComputedFingerprintsAsync = token => ReadActiveComputedFingerprintsAsync(store, manifest.Id, token),
            CheckModuleUpgradeAsync = token => CheckModuleUpgradeAsync(store, manifest.Id, release, token),
            InlineRecomputeLimit = inlineRecomputeLimit,
            TenantId = bindings.TenantId,
        };
        var provisioned = await EntityProvisioner.ProvisionAsync(model, data, migrations, cancellationToken);
        var diagnostics = new List<Diagnostic>([.. bindingDiagnostics, .. provisioned.Diagnostics]);
        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? new ActivationResult(diagnostics)
            : new ActivationResult(diagnostics) { Realm = realm.Plan };
    }

    // The realm plan from the tenant's other active releases and the release being activated, which replaces the
    // application's own active release. A provider whose bindings the tenant lacks is left out.
    private static async Task<RealmPlanResult> PlanRealmAsync(
        IActiveReleaseStore store, ApplicationModel model, ActivationBindings bindings, CancellationToken cancellationToken)
    {
        var applications = new List<RealmApplication>();
        foreach (var active in await store.ListAsync(cancellationToken))
        {
            if (active.ApplicationId == model.Manifest.Id
                || await store.GetReleaseAsync(active.ReleaseId, cancellationToken) is not { } stored)
            {
                continue;
            }

            applications.Add(RealmApplication.FromRelease(active.Name, stored));
        }

        applications.Add(RealmApplication.FromModel(model));
        return RealmBindingFilter.Plan(applications, bindings);
    }

    private static Diagnostic SplitsUser(RealmConflict conflict)
    {
        var provider = conflict.Provider;
        var other = conflict.Other.ApplicationId == RealmApplication.PlatformSignInId
            ? $"the provider '{conflict.OtherProvider.Name}' of the platform sign-in"
            : $"the provider '{conflict.OtherProvider.Name}' of the application '{conflict.Other.Name}'";
        return conflict.Kind == RealmConflictKind.ClientBindings
            ? new Diagnostic(
                DiagnosticCodes.IdentityProviderSplitsUser,
                $"The identity provider '{provider.Name}' names the issuer binding '{provider.IssuerBinding}' with other client bindings than {other}. The same person would become two users. Use the same client bindings.",
                provider.File,
                "/issuer/binding",
                provider.Id)
            : new Diagnostic(
                DiagnosticCodes.IdentityProviderSplitsUser,
                $"The identity provider '{provider.Name}' names the same bindings as {other} but other claims. The same person would become two users. Use the same claims.",
                provider.File,
                "/claims",
                provider.Id);
    }

    // The module upgrade the release makes over the application's active release; none before the first activation.
    private static async Task<IReadOnlyList<Diagnostic>> CheckModuleUpgradeAsync(
        IActiveReleaseStore store, Guid applicationId, Release release, CancellationToken cancellationToken)
    {
        if (await store.FindByApplicationIdAsync(applicationId, cancellationToken) is not { } active
            || active.ReleaseId == release.Id
            || await store.GetReleaseAsync(active.ReleaseId, cancellationToken) is not { } stored)
        {
            return [];
        }

        return ModuleUpgradeCheck.Check(stored.Resources, release.Resources);
    }

    // The entity ids of the application's active release by entity name: the tables that hold its data now.
    private static async Task<IReadOnlyDictionary<string, Guid>> ReadStoredEntityIdsAsync(IActiveReleaseStore store, Guid applicationId, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (await store.FindByApplicationIdAsync(applicationId, cancellationToken) is not { } active
            || await store.GetReleaseAsync(active.ReleaseId, cancellationToken) is not { } stored)
        {
            return ids;
        }

        foreach (var resource in stored.Resources)
        {
            using var document = JsonDocument.Parse(resource.Content);
            var root = document.RootElement;
            if (root.TryGetProperty("kind", out var kind) && kind.GetString() == "entity"
                && root.TryGetProperty("name", out var name) && name.GetString() is { } entityName
                && root.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var entityId))
            {
                ids[entityName] = entityId;
            }
        }

        return ids;
    }

    // The application's active release with its fingerprints; none before the first activation.
    private static async Task<ActiveComputedFingerprints> ReadActiveComputedFingerprintsAsync(IActiveReleaseStore store, Guid applicationId, CancellationToken cancellationToken) =>
        await store.FindByApplicationIdAsync(applicationId, cancellationToken) is { } active
            ? new ActiveComputedFingerprints(active.ReleaseId, await store.GetComputedFingerprintsAsync(active.ReleaseId, cancellationToken))
            : new ActiveComputedFingerprints(null, new Dictionary<Guid, string>());

    // The other releases of the application with open instances, and the fields their stored entities have.
    private static async Task<IReadOnlyList<OldReleaseWork>> ReadOldReleaseWorkAsync(
        IActiveReleaseStore store,
        IOpenInstanceCounter openInstances,
        DbTransaction transaction,
        Guid applicationId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var work = new List<OldReleaseWork>();
        var counts = await openInstances.CountByReleaseAsync(transaction, applicationId, cancellationToken);
        foreach (var (id, count) in counts.OrderBy(pair => pair.Key))
        {
            if (id == releaseId || count == 0)
            {
                continue;
            }

            if (await store.GetReleaseAsync(id, cancellationToken) is not { } stored)
            {
                work.Add(new OldReleaseWork(id, count, null));
                continue;
            }

            var fields = ReleaseColumns.FieldsByEntityId(stored);
            work.Add(new OldReleaseWork(id, count, fields));
        }

        return work;
    }

    private static List<Diagnostic> ToDiagnostics(ApplicationModel model, SetActiveReleaseResult set) =>
        set.Conflict switch
        {
            ActiveReleaseConflict.None => [],
            ActiveReleaseConflict.Name => [NameActiveForOtherApplication(model)],
            ActiveReleaseConflict.SitePath => [SitePathActiveForOtherApplication(model.Sites.Single(site => site.Path == set.SitePath))],
            _ => throw new InvalidOperationException($"Unknown active release conflict '{set.Conflict}'."),
        };

    /// <summary>
    /// Reports every binding the tenant lacks or holds with another kind: the file storage binding of the
    /// application, those of the connectors, those of the <c>oidc</c> identity providers (<c>issuer</c> as <c>url</c>, the
    /// client bindings as <c>secret</c>), the <c>metadata</c> binding of the <c>saml</c> identity providers (as <c>url</c>), and the <c>url</c> and <c>secret</c> bindings that a package operation step uses declares. Also
    /// reports every base URL that is not <c>https</c> when <c>http</c> is not allowed. A missing binding is a warning
    /// on a preview tenant. A message names the tenant and the binding, never its value.
    /// </summary>
    private static List<Diagnostic> CheckBindings(ApplicationModel model, ActivationBindings bindings)
    {
        var diagnostics = new List<Diagnostic>();
        if (model.Manifest.FileStorage is { } storage)
        {
            void Report(string code, string message, string path, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
                diagnostics.Add(new Diagnostic(code, message, model.Manifest.File, path, model.Manifest.Id, severity));

            CheckBinding(storage.Binding, ActivationBinding.StorageKind, "/fileStorage/binding", bindings, Report);
        }

        foreach (var connector in model.Connectors)
        {
            void Report(string code, string message, string path, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
                diagnostics.Add(new Diagnostic(code, message, connector.File, path, connector.Id, severity));

            var baseUrl = CheckBinding(connector.BaseUrlBinding, ActivationBinding.UrlKind, "/baseUrl/binding", bindings, Report);
            if (baseUrl is not null && !bindings.AllowHttp && baseUrl.Url?.Scheme != Uri.UriSchemeHttps)
            {
                Report(
                    DiagnosticCodes.InsecureBaseUrl,
                    $"Tenant '{bindings.TenantId}' binding '{connector.BaseUrlBinding}' is not an https URL. Only Development allows http.",
                    "/baseUrl/binding");
            }

            if (connector.Auth is { } auth)
            {
                CheckBinding(auth.Binding, ActivationBinding.SecretKind, auth.BindingPath, bindings, Report);
            }
        }

        foreach (var provider in model.IdentityProviders.Where(provider => provider.Type == "oidc"))
        {
            void Report(string code, string message, string path, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
                diagnostics.Add(new Diagnostic(code, message, provider.File, path, provider.Id, severity));

            CheckBinding(provider.IssuerBinding, ActivationBinding.UrlKind, "/issuer/binding", bindings, Report);
            CheckBinding(provider.ClientIdBinding, ActivationBinding.SecretKind, "/clientId/binding", bindings, Report);
            CheckBinding(provider.ClientSecretBinding, ActivationBinding.SecretKind, "/clientSecret/binding", bindings, Report);
            CheckBinding(provider.ClientKeyBinding, ActivationBinding.SecretKind, "/clientKey/binding", bindings, Report);
        }

        foreach (var provider in model.IdentityProviders.Where(provider => provider.Type == "saml"))
        {
            void Report(string code, string message, string path, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
                diagnostics.Add(new Diagnostic(code, message, provider.File, path, provider.Id, severity));

            CheckBinding(provider.MetadataBinding, ActivationBinding.UrlKind, "/metadata/binding", bindings, Report);
        }

        foreach (var process in model.Processes)
        {
            for (var index = 0; index < process.Steps.Count; index++)
            {
                if (process.Steps[index] is not PackageOperationStepModel step)
                {
                    continue;
                }

                var path = $"/steps/{index}/operation";
                void Report(string code, string message, string path, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
                    diagnostics.Add(new Diagnostic(code, message, process.File, path, process.Id, severity));

                foreach (var name in step.Capabilities.Urls)
                {
                    var url = CheckBinding(name, ActivationBinding.UrlKind, path, bindings, Report);
                    if (url is not null && !bindings.AllowHttp && url.Url?.Scheme != Uri.UriSchemeHttps)
                    {
                        Report(
                            DiagnosticCodes.InsecureBaseUrl,
                            $"Tenant '{bindings.TenantId}' binding '{name}' is not an https URL. Only Development allows http.",
                            path);
                    }
                }

                foreach (var name in step.Capabilities.Secrets)
                {
                    CheckBinding(name, ActivationBinding.SecretKind, path, bindings, Report);
                }
            }
        }

        return diagnostics;
    }

    /// <summary>Gives the binding named <paramref name="name"/> when the tenant has it with <paramref name="kind"/>, otherwise reports it.</summary>
    private static ActivationBinding? CheckBinding(
        string? name, string kind, string path, ActivationBindings bindings, Action<string, string, string, DiagnosticSeverity> report)
    {
        if (name is null)
        {
            return null;
        }

        var binding = bindings.Bindings.TryGetValue(name, out var exact)
            ? exact
            : bindings.Bindings.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (binding is null)
        {
            report(
                DiagnosticCodes.MissingDeploymentBinding,
                $"Tenant '{bindings.TenantId}' has no binding '{name}'. Add a '{kind}' binding with that name to the tenant.",
                path,
                bindings.IsPreview ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error);
            return null;
        }

        if (binding.Kind != kind)
        {
            report(
                DiagnosticCodes.MissingDeploymentBinding,
                $"Tenant '{bindings.TenantId}' binding '{name}' is a '{binding.Kind}' binding. It is used here as a '{kind}' binding.",
                path,
                DiagnosticSeverity.Error);
            return null;
        }

        return binding;
    }

    private static Diagnostic NameActiveForOtherApplication(ApplicationModel model) =>
        new(
            DiagnosticCodes.ApplicationNameActiveForOtherApplication,
            $"The application name '{model.Manifest.Name}' is already active for another application. Give the application another name.",
            model.Manifest.File,
            "/name",
            model.Manifest.Id);

    private static Diagnostic SitePathActiveForOtherApplication(SiteModel site) =>
        new(
            DiagnosticCodes.SitePathActiveForOtherApplication,
            $"The site path '{site.Path}' is already active for another application. Give the site another path.",
            site.File,
            "/path",
            site.Id);
}
