using Axis.Tenancy;

namespace Axis.Server.CellApi;

/// <summary>
/// The platform hosts of a cell: <c>Cell:ConsoleHost</c> and the host of <c>Cell:InternalUrl</c>. They belong to no tenant,
/// so request resolution gives them no tenant and serves only the cell's own routes there. The console host also serves the SPA's page and its two shell reads. A host that is not in a cell has none.
/// </summary>
internal sealed class CellHosts
{
    private readonly string[] _hosts;

    public CellHosts(CellOptions cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var hosts = new List<string>();
        if (cell.IsCell)
        {
            if (!string.IsNullOrWhiteSpace(cell.ConsoleHost))
            {
                hosts.Add(cell.ConsoleHost.Trim());
            }

            if (!string.IsNullOrWhiteSpace(cell.InternalUrl))
            {
                if (!Uri.TryCreate(cell.InternalUrl.Trim(), UriKind.Absolute, out var internalUrl))
                {
                    throw new InvalidOperationException("The setting Cell:InternalUrl must be an absolute URL.");
                }

                hosts.Add(internalUrl.Host);
            }
        }

        _hosts = [.. hosts];
    }

    /// <summary>Whether the cell has at least one platform host.</summary>
    public bool Any => _hosts.Length > 0;

    /// <summary>Whether <paramref name="host"/>, without a port, is a platform host, in any letter case.</summary>
    public bool IsPlatformHost(string? host) =>
        !string.IsNullOrEmpty(host) && _hosts.Contains(host, StringComparer.OrdinalIgnoreCase);
}
