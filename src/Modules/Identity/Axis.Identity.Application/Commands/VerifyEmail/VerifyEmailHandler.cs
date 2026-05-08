using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(IUserRepository userRepo, IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(command.Token, out var userId))
            throw new ValidationException("Invalid verification link.");

        var user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            throw new ValidationException("Invalid verification link.");

        if (user.IsEmailVerified)
            throw new ValidationException("This link has already been used. Please sign in.");

        user.VerifyEmail();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
