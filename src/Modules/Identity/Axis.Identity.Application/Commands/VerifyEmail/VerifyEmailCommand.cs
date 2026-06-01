using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public record VerifyEmailCommand(string Token) : ICommand<VerifyEmailSuccessDto>;
