using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.UpdateWorkspaceProfile;

public sealed class UpdateWorkspaceProfileHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceLogoStorageService logoStorage,
    IUnitOfWork uow)
    : ICommandHandler<UpdateWorkspaceProfileCommand>
{
    private const int MaxLogoBytes = 2_097_152; // 2 MB
    private static readonly HashSet<string> AllowedLogoTypes =
        ["image/jpeg", "image/png", "image/svg+xml"];

    public async Task<Result> Handle(UpdateWorkspaceProfileCommand command, CancellationToken cancellationToken)
    {
        if (command.LogoBytes is not null)
        {
            if (command.LogoContentType is null || !AllowedLogoTypes.Contains(command.LogoContentType))
                return Result.Failure(ErrorCodes.BusinessRule, "Logo must be PNG, JPG, or SVG.");

            if (command.LogoBytes.Length > MaxLogoBytes)
                return Result.Failure(ErrorCodes.BusinessRule, "Logo must not exceed 2 MB.");
        }

        if (!WorkspaceTimeZoneValidator.IsValidIanaTimeZone(command.TimeZoneId))
            return Result.Failure(ErrorCodes.BusinessRule, "Timezone must be a valid IANA timezone.");

        if (!WorkspaceLanguageValidator.IsValid(command.DefaultLanguage))
            return Result.Failure(ErrorCodes.BusinessRule, "Default language must be a valid language tag (e.g. en or en-US).");

        Workspace? Workspace = await workspaceRepo.GetByIdAsync(command.workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        try
        {
            Workspace.UpdateProfile(command.Name, command.TimeZoneId, command.DefaultLanguage);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        string? oldLogoUrl = null;
        string? uploadedLogoUrl = null;

        if (command.LogoBytes is not null)
        {
            oldLogoUrl = Workspace.LogoUrl;
            uploadedLogoUrl = await logoStorage.UploadLogoAsync(
                command.workspaceId,
                command.LogoBytes,
                command.LogoContentType!,
                cancellationToken);
            Workspace.UpdateLogoUrl(uploadedLogoUrl);
        }

        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (uploadedLogoUrl is not null)
            {
                try
                {
                    await logoStorage.DeleteLogoAsync(uploadedLogoUrl, cancellationToken);
                }
                catch
                {
                    // Best-effort rollback of orphaned upload.
                }
            }

            throw;
        }

        if (oldLogoUrl is not null && uploadedLogoUrl is not null)
        {
            try
            {
                await logoStorage.DeleteLogoAsync(oldLogoUrl, cancellationToken);
            }
            catch
            {
                // Best-effort cleanup; profile update already committed.
            }
        }

        return Result.Success();
    }
}
