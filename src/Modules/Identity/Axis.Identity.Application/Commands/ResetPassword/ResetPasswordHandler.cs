using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordHandler(
    IUserRepository userRepo,
    IPasswordResetTokenStore tokenStore,
    IPasswordHasher hasher,
    IUnitOfWork uow)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        if (command.NewPassword != command.PasswordConfirmation)
            return Result.Failure(ErrorCodes.BusinessRule, "Passwords do not match.");

        string? passwordError = PasswordPolicy.Validate(command.NewPassword);
        if (passwordError is not null)
            return Result.Failure(ErrorCodes.BusinessRule, passwordError);

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token);

        Guid? userId = await tokenStore.FindUserIdByTokenHashAsync(tokenHash, cancellationToken);
        if (userId is null)
            return Result.Failure(ErrorCodes.BusinessRule, "This reset link has expired. Please request a new one.");

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.BusinessRule, "This reset link has expired. Please request a new one.");

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        await tokenStore.InvalidateAsync(tokenHash, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
