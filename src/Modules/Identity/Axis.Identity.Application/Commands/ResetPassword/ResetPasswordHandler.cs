using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordHandler(
    IUserRepository userRepo,
    IPasswordResetTokenStore tokenStore,
    IPasswordHasher hasher,
    IUnitOfWork uow)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        if (command.NewPassword != command.PasswordConfirmation)
            throw new ValidationException("Passwords do not match.");

        if (!IsStrongPassword(command.NewPassword))
            throw new ValidationException(
                "Password must be at least 8 characters and contain at least one letter and one number.");

        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(command.Token)));

        var userId = await tokenStore.FindUserIdByTokenHashAsync(tokenHash, cancellationToken);
        if (userId is null)
            throw new ValidationException("This reset link has expired. Please request a new one.");

        var user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null)
            throw new ValidationException("This reset link has expired. Please request a new one.");

        user.SetPasswordHash(hasher.Hash(command.NewPassword));
        await tokenStore.InvalidateAsync(tokenHash, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
    }

    private static bool IsStrongPassword(string password) =>
        password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}
