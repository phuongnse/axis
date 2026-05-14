namespace Axis.Api.Endpoints;

public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
