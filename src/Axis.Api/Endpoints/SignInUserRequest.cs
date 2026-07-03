namespace Axis.Api.Endpoints;

public sealed record SignInUserRequest(
    string Email,
    string Password);
