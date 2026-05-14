namespace Axis.Api.Endpoints;

public record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
