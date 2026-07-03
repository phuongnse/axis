using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateUserLanguagePreference;

public sealed class UpdateUserLanguagePreferenceHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateUserLanguagePreferenceCommand, LanguagePreferenceDto>
{
    public async Task<Result<LanguagePreferenceDto>> Handle(
        UpdateUserLanguagePreferenceCommand command,
        CancellationToken cancellationToken)
    {
        Result<UserLanguage> language = UserLanguage.Create(command.Language);
        if (language.IsFailure)
        {
            return Result.Failure<LanguagePreferenceDto>(
                ErrorCodes.InvalidInput,
                language.Error);
        }

        User? user = await userRepository.GetByIdPlatformWideAsync(
            command.UserId,
            cancellationToken);
        if (user is null)
        {
            return Result.Failure<LanguagePreferenceDto>(
                ErrorCodes.NotFound,
                "User was not found.");
        }

        user.SetLanguagePreference(language.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new LanguagePreferenceDto(language.Value.Value));
    }
}
