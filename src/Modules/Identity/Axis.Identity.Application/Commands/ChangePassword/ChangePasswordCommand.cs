using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ChangePassword;

public record ChangePasswordCommand(
    Guid UserId,
    Guid TeamAccountId,
    string CurrentPassword,
    string NewPassword,
    string PasswordConfirmation) : ICommand;
