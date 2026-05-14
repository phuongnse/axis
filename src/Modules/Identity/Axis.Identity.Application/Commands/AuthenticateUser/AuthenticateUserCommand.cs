using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.AuthenticateUser;

public record AuthenticateUserCommand(string Email, string Password) : ICommand<AuthenticationResult>;
