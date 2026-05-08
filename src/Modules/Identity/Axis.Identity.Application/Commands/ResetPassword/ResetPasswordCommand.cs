using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ResetPassword;

public record ResetPasswordCommand(
    string Token,
    string NewPassword,
    string PasswordConfirmation) : ICommand;
