using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateUserThemePreference;

public sealed class UpdateUserThemePreferenceHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateUserThemePreferenceCommand, ThemePreferenceDto>
{
    public async Task<Result<ThemePreferenceDto>> Handle(
        UpdateUserThemePreferenceCommand command,
        CancellationToken cancellationToken)
    {
        Result<UserTheme> theme = UserTheme.Create(command.Theme);
        if (theme.IsFailure)
        {
            return Result.Failure<ThemePreferenceDto>(
                ErrorCodes.InvalidInput,
                theme.Error);
        }

        User? user = await userRepository.GetByIdPlatformWideAsync(
            command.UserId,
            cancellationToken);
        if (user is null)
        {
            return Result.Failure<ThemePreferenceDto>(
                ErrorCodes.NotFound,
                "User was not found.");
        }

        user.SetThemePreference(theme.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ThemePreferenceDto(theme.Value.Value));
    }
}
