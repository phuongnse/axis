using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.UpdateUserProfile;

public sealed class UpdateUserProfileHandler(
    IUserRepository userRepo,
    IAvatarStorageService avatarStorage,
    IUnitOfWork uow)
    : ICommandHandler<UpdateUserProfileCommand>
{
    private const int MaxAvatarBytes = 1_048_576; // 1 MB
    private static readonly HashSet<string> AllowedAvatarTypes = ["image/jpeg", "image/png"];

    public async Task Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
    {
        var fullName = $"{command.FirstName} {command.LastName}".Trim();
        if (fullName.Length < 2 || fullName.Length > 100)
            throw new ValidationException("Full name must be between 2 and 100 characters.");

        if (command.AvatarBytes is not null)
        {
            if (command.AvatarContentType is null || !AllowedAvatarTypes.Contains(command.AvatarContentType))
                throw new ValidationException("Avatar must be PNG or JPG.");

            if (command.AvatarBytes.Length > MaxAvatarBytes)
                throw new ValidationException("Avatar must not exceed 1 MB.");
        }

        var user = await userRepo.GetByIdAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (user is null)
            throw new ValidationException("User not found.");

        user.UpdateProfile(command.FirstName, command.LastName);

        if (command.AvatarBytes is not null)
        {
            var oldAvatarUrl = user.AvatarUrl;

            var newUrl = await avatarStorage.UploadAvatarAsync(
                command.UserId, command.AvatarBytes, command.AvatarContentType!, cancellationToken);

            user.UpdateAvatar(newUrl);

            if (oldAvatarUrl is not null)
                await avatarStorage.DeleteAvatarAsync(oldAvatarUrl, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
