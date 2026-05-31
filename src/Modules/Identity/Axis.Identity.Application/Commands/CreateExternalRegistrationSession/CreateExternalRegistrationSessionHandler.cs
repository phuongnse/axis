using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CreateExternalRegistrationSession;

public sealed class CreateExternalRegistrationSessionHandler(
    IExternalRegistrationSessionRepository sessionRepo,
    IUserRepository userRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateExternalRegistrationSessionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateExternalRegistrationSessionCommand command,
        CancellationToken cancellationToken)
    {
        Result<Email> emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
            return Result.Failure<Guid>(emailResult.ErrorCode ?? ErrorCodes.InvalidInput, emailResult.Error);

        if (await userRepo.EmailExistsPlatformWideAsync(emailResult.Value, cancellationToken))
        {
            return Result.Failure<Guid>(
                ErrorCodes.Conflict,
                "An account with this email already exists. Sign in instead.");
        }

        ExternalRegistrationSession session = ExternalRegistrationSession.Create(
            command.Provider,
            command.ProviderKey,
            emailResult.Value,
            command.DisplayName);

        await sessionRepo.AddAsync(session, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(session.Id);
    }
}
