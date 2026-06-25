using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ResendVerificationEmail;

public record ResendVerificationEmailCommand(string Email) : ICommand;
