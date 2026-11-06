namespace Axis.ControlPlane.Cells;

/// <summary>A tenant of a cell's tenant list, with the hosts that route to it.</summary>
internal sealed record CellTenant(string Id, IReadOnlyList<string> Hosts);

/// <summary>The body of <c>GET /api/cell/tenants</c>, of which the client reads only the id and the hosts.</summary>
internal sealed record CellTenantList(IReadOnlyList<CellTenantListItem?>? Items);

/// <summary>One item of <see cref="CellTenantList"/>.</summary>
internal sealed record CellTenantListItem(string? Id, IReadOnlyList<string>? Hosts);
