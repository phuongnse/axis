namespace Axis.ControlPlane.Cells;

/// <summary>A tenant of a cell's tenant list, with the hosts that route to it.</summary>
internal sealed record CellTenant(string Id, IReadOnlyList<string> Hosts);

/// <summary>The body of <c>GET /api/cell/tenants</c>, of which the client reads only the id and the hosts.</summary>
internal sealed record CellTenantList(IReadOnlyList<CellTenantListItem?>? Items);

/// <summary>One item of <see cref="CellTenantList"/>.</summary>
internal sealed record CellTenantListItem(string? Id, IReadOnlyList<string>? Hosts);

/// <summary>An administrator of a tenant, read from <c>GET /api/cell/tenants/{id}/administrators</c>. The language is the locale chosen in the realm, or null.</summary>
internal sealed record CellAdministrator(string Email, string? DisplayName, string? Language);

/// <summary>The body of <c>GET /api/cell/tenants/{id}/administrators</c>.</summary>
internal sealed record CellAdministratorList(IReadOnlyList<CellAdministratorItem?>? Items);

/// <summary>One item of <see cref="CellAdministratorList"/>.</summary>
internal sealed record CellAdministratorItem(string? Email, string? DisplayName, string? Language);
