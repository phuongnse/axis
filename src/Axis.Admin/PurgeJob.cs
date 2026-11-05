using Axis.Tenancy.Platform;

namespace Axis.Admin;

/// <summary>
/// The <c>purge</c> job of the publisher in a cell: the eight steps of <see cref="TenantPurge"/>, which the publisher queues for a main tenant whose
/// deletion window has ended. The purge reads the admin database role and the identity provider admin client of the create job, and
/// <c>Studio:ApplicationsPath</c>. When one of them is missing, every step refuses with the problem that names the setting, so the first step fails the job and
/// nothing is removed. A missing <c>Studio:ApplicationsPath</c> is never read as no folder, because in a cell every tenant has one.
/// </summary>
internal static class PurgeJob
{
    public const string Kind = "purge";

    // The names of TenantPurge's steps, which the refusing steps keep so the stored steps of a failed job read the same either way.
    private static readonly string[] _names = ["hosts", "database", "realm", "files", "studio", "keys", "rows", "directory"];

    public static IReadOnlyList<JobStep> Steps(
        JobSetting<CreateSettings> create,
        JobSetting<string> applicationsPath,
        string platformConnectionString,
        TenantSecretCipher cipher,
        HttpClient http)
    {
        if ((create.Problem ?? applicationsPath.Problem) is { } problem)
        {
            return [.. _names.Select(name => new JobStep(name, (_, _, _, _) => Task.FromResult<string?>(problem)))];
        }

        var settings = create.Value!;
        return new TenantPurge(settings.Admin, platformConnectionString, cipher, http, settings.IdentityProvider!, applicationsPath.Value).Steps();
    }
}
