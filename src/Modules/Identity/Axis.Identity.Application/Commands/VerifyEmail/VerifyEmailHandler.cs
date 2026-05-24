using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(IUserRepository userRepo, IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(command.Token, out Guid userId))
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (user.IsEmailVerified)
            return Result.Failure(ErrorCodes.BusinessRule, "This link has already been used. Please sign in.");

        // VerifyEmail raises OrganizationVerified domain event; IdentityUnitOfWork maps to
        // OrganizationVerifiedEvent (Avro) and publishes via Wolverine → Kafka (ADR-019).
        // Each module's OrganizationVerifiedHandler subscribes and provisions its own schema.
        user.VerifyEmail();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
