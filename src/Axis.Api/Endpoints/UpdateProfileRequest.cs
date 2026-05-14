namespace Axis.Api.Endpoints;

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? AvatarBase64,
    string? AvatarContentType);
