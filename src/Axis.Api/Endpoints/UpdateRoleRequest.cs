namespace Axis.Api.Endpoints;

public record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
