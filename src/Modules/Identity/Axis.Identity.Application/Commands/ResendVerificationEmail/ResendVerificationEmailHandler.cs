using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class ResendVerificationEmailHandler(IUserRepository userRepo, IEmailSender emailSender)
    : ICommandHandler<ResendVerificationEmailCommand>
{
    public async Task<Result> Handle(ResendVerificationEmailCommand command, CancellationToken cancellationToken)
    {
        Result<Email> email = Email.Create(command.Email);
        if (email.IsFailure) return Result.Success(); // no error leakage per US-002

        User? user = await userRepo.FindByEmailGloballyAsync(email.Value, cancellationToken);
        if (user is null || user.IsEmailVerified) return Result.Success(); // silent — no info leakage

        // Rate limiting is an Infrastructure/API concern; Application layer just sends
        await emailSender.SendVerificationEmailAsync(
            user.Email.Value, user.Id.ToString(), cancellationToken);

        return Result.Success();
    }
}
