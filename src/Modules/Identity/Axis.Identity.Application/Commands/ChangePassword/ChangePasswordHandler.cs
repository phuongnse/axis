using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordHandler(
    IUserRepository userRepo,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdAsync(command.UserId, command.TeamAccountId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash ?? string.Empty))
            return Result.Failure(ErrorCodes.BusinessRule, "Current password is incorrect.");

        if (command.NewPassword != command.PasswordConfirmation)
            return Result.Failure(ErrorCodes.BusinessRule, "Passwords do not match.");

        if (hasher.Verify(command.NewPassword, user.PasswordHash ?? string.Empty))
            return Result.Failure(ErrorCodes.BusinessRule, "New password must be different from your current password.");

        string? passwordError = PasswordPolicy.Validate(
            command.NewPassword,
            user.Email.Value,
            user.FirstName,
            user.LastName);
        if (passwordError is not null)
            return Result.Failure(ErrorCodes.BusinessRule, passwordError);

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        await uow.SaveChangesAsync(cancellationToken);

        // Notification failure must not roll back the password change
        try
        {
            await emailSender.SendPasswordChangedNotificationAsync(
                user.Email.Value, cancellationToken);
        }
        catch
        {
            // Logged separately at infrastructure level — intentionally swallowed here
        }

        return Result.Success();
    }
}
