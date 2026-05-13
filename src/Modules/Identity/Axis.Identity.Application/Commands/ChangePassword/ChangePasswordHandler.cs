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
        User? user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash ?? string.Empty))
            return Result.Failure(ErrorCodes.BusinessRule, "Current password is incorrect.");

        if (command.NewPassword != command.PasswordConfirmation)
            return Result.Failure(ErrorCodes.BusinessRule, "Passwords do not match.");

        if (hasher.Verify(command.NewPassword, user.PasswordHash ?? string.Empty))
            return Result.Failure(ErrorCodes.BusinessRule, "New password must be different from your current password.");

        if (!IsStrongPassword(command.NewPassword))
            return Result.Failure(ErrorCodes.BusinessRule,
                "Password must be at least 8 characters and contain at least one letter and one number.");

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        await uow.SaveChangesAsync(cancellationToken);

        // Notification failure must not roll back the password change (per US-028)
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

    private static bool IsStrongPassword(string password) =>
        password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}
