namespace Axis.ControlPlane.Cells;

/// <summary>The clients of the cells of the region, one for each entry of <c>ControlPlane:Cells</c>.</summary>
internal sealed class CellClients : IDisposable
{
    private readonly Dictionary<string, CellClient> _clients;

    public CellClients(ControlPlaneOptions options, IHttpClientFactory httpClients, CellClientOptions clientOptions, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClients);
        ArgumentNullException.ThrowIfNull(clientOptions);
        ArgumentNullException.ThrowIfNull(clock);

        _clients = options.Cells.ToDictionary(
            cell => cell.Id!,
            cell => new CellClient(
                cell, options.CellSecrets[cell.Id!], () => httpClients.CreateClient(CellClientsServiceCollectionExtensions.HttpClientName), clientOptions, clock),
            StringComparer.Ordinal);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }

    public IReadOnlyList<CellClient> All => [.. _clients.Values];

    /// <exception cref="ArgumentException">No cell of the region has this id.</exception>
    public CellClient Get(string cellId) =>
        _clients.TryGetValue(cellId, out var client)
            ? client
            : throw new ArgumentException($"The region has no cell '{cellId}'.", nameof(cellId));
}
