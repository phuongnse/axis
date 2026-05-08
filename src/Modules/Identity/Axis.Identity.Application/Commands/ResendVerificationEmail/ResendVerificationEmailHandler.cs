using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class ResendVerificationEmailHandler(IUserRepository userRepo, IEmailSender emailSender)
    : ICommandHandler<ResendVerificationEmailCommand>
{
    public async Task Handle(ResendVerificationEmailCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure) return; // no error leakage per US-002

        var user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null || user.IsEmailVerified) return; // silent — no info leakage

        // Rate limiting is an Infrastructure/API concern; Application layer just sends
        await emailSender.SendVerificationEmailAsync(
            user.Email.Value, user.Id.ToString(), cancellationToken);
    }
}
