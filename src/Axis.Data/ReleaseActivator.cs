using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Storage;

namespace Axis.Data;

/// <summary>
/// Makes a compiled release the active release of its application: it checks that no other
/// application is active under the manifest name, provisions the entity tables and then sets the
/// active release.
/// <para>
/// Provisioning runs in its own transaction, and the active release is written afterwards in a
/// separate statement. No store call runs while that transaction is open, so the store and
/// <see cref="DataDbContext"/> may share one open tenant connection or use two. When writing the
/// active release fails after provisioning committed, the exception propagates: the new tables and
/// columns stay, recorded for the application, and the previous release stays active. Activating
/// again is safe because provisioning is additive.
/// </para>
/// </summary>
public static class ReleaseActivator
{
    /// <summary>
    /// Activates the release in <paramref name="compiled"/>, which must have a release and a model
    /// with the same application id. Call it outside an explicit transaction.
    /// </summary>
    public static async Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled,
        IActiveReleaseStore store,
        DataDbContext data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(data);
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

        var provisioned = await EntityProvisioner.ProvisionAsync(model, data, cancellationToken);
        if (provisioned.Diagnostics.Count > 0)
        {
            return new ActivationResult(provisioned.Diagnostics);
        }

        // Another application can take the name between the check above and this write.
        if (!await store.TrySetAsync(manifest.Id, manifest.Name, release.Id, DateTimeOffset.UtcNow, cancellationToken))
        {
            return new ActivationResult([NameActiveForOtherApplication(model)]);
        }

        return new ActivationResult([]);
    }

    private static Diagnostic NameActiveForOtherApplication(ApplicationModel model) =>
        new(
            DiagnosticCodes.ApplicationNameActiveForOtherApplication,
            $"The application name '{model.Manifest.Name}' is already active for another application. Give the application another name.",
            model.Manifest.File,
            "/name",
            model.Manifest.Id);
}
