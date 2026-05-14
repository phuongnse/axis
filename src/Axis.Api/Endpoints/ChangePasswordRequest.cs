namespace Axis.Api.Endpoints;

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);
