namespace Axis.Api.Endpoints;

public record InviteUserRequest(string Email, Guid RoleId);
