using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordHandler(
    IUserRepository userRepo,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            throw new ValidationException("User not found.");

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash ?? string.Empty))
            throw new ValidationException("Current password is incorrect.");

        if (command.NewPassword != command.PasswordConfirmation)
            throw new ValidationException("Passwords do not match.");

        if (hasher.Verify(command.NewPassword, user.PasswordHash ?? string.Empty))
            throw new ValidationException("New password must be different from your current password.");

        if (!IsStrongPassword(command.NewPassword))
            throw new ValidationException(
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
    }

    private static bool IsStrongPassword(string password) =>
        password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}
