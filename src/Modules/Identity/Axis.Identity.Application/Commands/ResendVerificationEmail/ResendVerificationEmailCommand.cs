using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public record ResendVerificationEmailCommand(string Email) : ICommand;
