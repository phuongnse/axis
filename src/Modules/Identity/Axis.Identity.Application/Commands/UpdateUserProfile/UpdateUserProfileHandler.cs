using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateUserProfile;

public sealed class UpdateUserProfileHandler(
    IUserRepository userRepo,
    IAvatarStorageService avatarStorage,
    IUnitOfWork uow)
    : ICommandHandler<UpdateUserProfileCommand>
{
    private const int MaxAvatarBytes = 1_048_576; // 1 MB
    private static readonly HashSet<string> AllowedAvatarTypes = ["image/jpeg", "image/png"];

    public async Task<Result> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
    {
        string fullName = $"{command.FirstName} {command.LastName}".Trim();
        if (fullName.Length < 2 || fullName.Length > 100)
            return Result.Failure(ErrorCodes.BusinessRule, "Full name must be between 2 and 100 characters.");

        if (command.AvatarBytes is not null)
        {
            if (command.AvatarContentType is null || !AllowedAvatarTypes.Contains(command.AvatarContentType))
                return Result.Failure(ErrorCodes.BusinessRule, "Avatar must be PNG or JPG.");

            if (command.AvatarBytes.Length > MaxAvatarBytes)
                return Result.Failure(ErrorCodes.BusinessRule, "Avatar must not exceed 1 MB.");
        }

        User? user = await userRepo.GetByIdAsync(command.UserId, command.tenantId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        user.UpdateProfile(command.FirstName, command.LastName);

        if (command.AvatarBytes is not null)
        {
            string? oldAvatarUrl = user.AvatarUrl;

            string newUrl = await avatarStorage.UploadAvatarAsync(
                command.UserId, command.AvatarBytes, command.AvatarContentType!, cancellationToken);

            user.UpdateAvatar(newUrl);

            if (oldAvatarUrl is not null)
                await avatarStorage.DeleteAvatarAsync(oldAvatarUrl, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
