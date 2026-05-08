using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RequestPasswordReset;

public record RequestPasswordResetCommand(string Email) : ICommand;
