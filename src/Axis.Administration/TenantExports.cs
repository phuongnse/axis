namespace Axis.Administration;

/// <summary>
/// What the server and the publisher share about a stored tenant export, see <c>docs/reference/tenant-lifecycle.md#exporting-a-tenant</c>:
/// where its ZIP lives, how long it is kept and the command of the audit record of its download.
/// </summary>
public static class TenantExports
{
    /// <summary>How long a ZIP is kept: the route answers that the export is gone after this time and the publisher's sweep deletes it.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    /// <summary>The command of the platform audit record that each download writes.</summary>
    public const string DownloadCommand = "tenant export download";

    /// <summary>The prefix of every ZIP in the export storage.</summary>
    public const string Prefix = "exports/";

    /// <summary>The object key of the ZIP of the export job <paramref name="exportId"/> of <paramref name="tenantId"/>.</summary>
    public static string Key(string tenantId, Guid exportId) => $"{Prefix}{tenantId}/{exportId:D}.zip";
}
