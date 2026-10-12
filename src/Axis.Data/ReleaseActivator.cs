using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Storage;

namespace Axis.Data;

/// <summary>
/// Makes a compiled release the active release of its application: it checks that no other
/// application is active under the manifest name or holds one of its site paths, checks that the
/// tenant has every deployment binding its connectors name with the right kind, provisions the
/// entity tables and then sets the active release with its site paths.
/// <para>
/// Provisioning runs in its own transaction, and the active release and its site paths are written
/// afterwards in a separate short transaction. No store call runs while the provisioning
/// transaction is open, so the store and <see cref="DataDbContext"/> may share one open tenant
/// connection or use two. When writing the active release fails after provisioning committed, the
/// exception propagates: the new tables and columns stay, recorded for the application, and the
/// previous release stays active. Activating again is safe because provisioning is additive.
/// </para>
/// </summary>
public static class ReleaseActivator
{
    /// <summary>
    /// Activates the release in <paramref name="compiled"/>, which must have a release and a model
    /// with the same application id, for the tenant whose deployment bindings are
    /// <paramref name="bindings"/>. Call it outside an explicit transaction.
    /// </summary>
    public static async Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled,
        IActiveReleaseStore store,
        DataDbContext data,
        ActivationBindings bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bindings);
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
        var badBindings = CheckBindings(model, bindings);
        if (badBindings.Count > 0)
        {
            return new ActivationResult(badBindings);
        }

        var provisioned = await EntityProvisioner.ProvisionAsync(model, data, cancellationToken);
        if (provisioned.Diagnostics.Count > 0)
        {
            return new ActivationResult(provisioned.Diagnostics);
        }

        // Another application can take the name or a site path between the checks above and this write.
        var sitePaths = model.Sites.Select(site => site.Path).ToList();
        var set = await store.TrySetAsync(manifest.Id, manifest.Name, release.Id, sitePaths, DateTimeOffset.UtcNow, cancellationToken);
        return set.Conflict switch
        {
            ActiveReleaseConflict.None => new ActivationResult([]),
            ActiveReleaseConflict.Name => new ActivationResult([NameActiveForOtherApplication(model)]),
            ActiveReleaseConflict.SitePath => new ActivationResult([SitePathActiveForOtherApplication(model.Sites.Single(site => site.Path == set.SitePath))]),
            _ => throw new InvalidOperationException($"Unknown active release conflict '{set.Conflict}'."),
        };
    }

    /// <summary>
    /// Reports every connector binding the tenant lacks or holds with another kind, and every base
    /// URL that is not <c>https</c> when <c>http</c> is not allowed. A message names the tenant
    /// and the binding, never its value.
    /// </summary>
    private static List<Diagnostic> CheckBindings(ApplicationModel model, ActivationBindings bindings)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var connector in model.Connectors)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, connector.File, path, connector.Id));

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

        return diagnostics;
    }

    /// <summary>Gives the binding named <paramref name="name"/> when the tenant has it with <paramref name="kind"/>, otherwise reports it.</summary>
    private static ActivationBinding? CheckBinding(
        string name, string kind, string path, ActivationBindings bindings, Action<string, string, string> report)
    {
        var binding = bindings.Bindings.TryGetValue(name, out var exact)
            ? exact
            : bindings.Bindings.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (binding is null)
        {
            report(
                DiagnosticCodes.MissingDeploymentBinding,
                $"Tenant '{bindings.TenantId}' has no binding '{name}'. Add a '{kind}' binding with that name to the tenant.",
                path);
            return null;
        }

        if (binding.Kind != kind)
        {
            report(
                DiagnosticCodes.MissingDeploymentBinding,
                $"Tenant '{bindings.TenantId}' binding '{name}' is a '{binding.Kind}' binding. It is used here as a '{kind}' binding.",
                path);
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
