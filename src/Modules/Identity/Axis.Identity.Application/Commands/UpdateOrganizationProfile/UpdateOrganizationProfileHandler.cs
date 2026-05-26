using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateOrganizationProfile;

public sealed class UpdateOrganizationProfileHandler(
    IOrganizationRepository orgRepo,
    IOrganizationLogoStorageService logoStorage,
    IUnitOfWork uow)
    : ICommandHandler<UpdateOrganizationProfileCommand>
{
    private const int MaxLogoBytes = 2_097_152; // 2 MB
    private static readonly HashSet<string> AllowedLogoTypes =
        ["image/jpeg", "image/png", "image/svg+xml"];

    public async Task<Result> Handle(UpdateOrganizationProfileCommand command, CancellationToken cancellationToken)
    {
        if (command.LogoBytes is not null)
        {
            if (command.LogoContentType is null || !AllowedLogoTypes.Contains(command.LogoContentType))
                return Result.Failure(ErrorCodes.BusinessRule, "Logo must be PNG, JPG, or SVG.");

            if (command.LogoBytes.Length > MaxLogoBytes)
                return Result.Failure(ErrorCodes.BusinessRule, "Logo must not exceed 2 MB.");
        }

        if (!OrganizationTimeZoneValidator.IsValidIanaTimeZone(command.TimeZoneId))
            return Result.Failure(ErrorCodes.BusinessRule, "Timezone must be a valid IANA timezone.");

        Organization? organization = await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        try
        {
            organization.UpdateProfile(command.Name, command.TimeZoneId, command.DefaultLanguage);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        if (command.LogoBytes is not null)
        {
            string? oldLogoUrl = organization.LogoUrl;
            string newUrl = await logoStorage.UploadLogoAsync(
                command.OrganizationId,
                command.LogoBytes,
                command.LogoContentType!,
                cancellationToken);
            organization.UpdateLogoUrl(newUrl);

            if (oldLogoUrl is not null)
                await logoStorage.DeleteLogoAsync(oldLogoUrl, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
