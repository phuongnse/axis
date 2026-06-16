using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailHandler(
    IEmailVerificationTokenStore tokenStore,
    IWorkspaceRegistrationTokenStore WorkspaceTokenStore,
    IUserRepository userRepo,
    IWorkspaceMembershipRepository membershipRepo,
    IWorkspaceRepository WorkspaceRepo,
    IWorkspaceModuleProvisioningRepository provisioningRepo,
    IRoleRepository roleRepo,
    IUnitOfWork uow)
    : ICommandHandler<VerifyEmailCommand, VerifyEmailSuccessDto>
{
    private static readonly TimeSpan FirstUserSetupTokenLifetime = TimeSpan.FromHours(24);

    public async Task<Result<VerifyEmailSuccessDto>> Handle(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        string tokenHash = OpaqueTokenGenerator.Hash(command.Token.Trim());
        EmailVerificationTokenResolveResult resolved =
            await tokenStore.ResolveForVerificationAsync(tokenHash, cancellationToken);

        return resolved.State switch
        {
            EmailVerificationTokenState.NotFound =>
                await VerifyWorkspaceContactAsync(tokenHash, cancellationToken),
            EmailVerificationTokenState.Expired =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This verification link has expired. Please request a new verification email."),
            EmailVerificationTokenState.AlreadyUsed =>
                Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "This link has already been used. Please sign in."),
            EmailVerificationTokenState.Valid => await VerifyUserAsync(
                resolved.UserId!.Value,
                cancellationToken),
            _ => Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link."),
        };
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyWorkspaceContactAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        Result<Guid> resolved =
            await WorkspaceTokenStore.ResolveVerificationAsync(tokenHash, cancellationToken);

        return resolved.IsFailure
            ? Result.Failure<VerifyEmailSuccessDto>(
                resolved.ErrorCode ?? ErrorCodes.BusinessRule,
                resolved.Error)
            : await VerifyWorkspaceAsync(resolved.Value, cancellationToken);
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        Workspace? Workspace = await WorkspaceRepo.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (Workspace.Status != WorkspaceStatus.PendingVerification)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        Workspace.BeginProvisioningAfterContactVerification();

        List<WorkspaceModuleProvisioning> pendingModules = WorkspaceModuleNames.All
            .Select(module => WorkspaceModuleProvisioning.CreatePending(Workspace.Id, module))
            .ToList();
        await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);

        (string rawSetupToken, string setupTokenHash) = OpaqueTokenGenerator.Create();
        await WorkspaceTokenStore.CreateFirstUserSetupAsync(
            Workspace.Id,
            setupTokenHash,
            DateTime.UtcNow.Add(FirstUserSetupTokenLifetime),
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            null,
            Workspace.Id,
            Workspace.OwnerEmail.Value,
            Workspace.Name,
            [],
            VerifyEmailNextStep.RegisterUser,
            rawSetupToken));
    }

    private async Task<Result<VerifyEmailSuccessDto>> VerifyUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

        if (user.IsEmailVerified)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "This link has already been used. Please sign in.");
        }

        IReadOnlyList<WorkspaceMembership> memberships =
            await membershipRepo.GetByUserIdAsync(user.Id, cancellationToken);
        List<(WorkspaceMembership Membership, Workspace Workspace)> activeWorkspaces = [];

        foreach (WorkspaceMembership membership in memberships.Where(m => m.Status == WorkspaceMembershipStatus.Active))
        {
            Workspace? workspace = await WorkspaceRepo.GetByIdAsync(membership.workspaceId, cancellationToken);
            if (workspace is null)
                return Result.Failure<VerifyEmailSuccessDto>(ErrorCodes.BusinessRule, "Invalid verification link.");

            if (workspace.Status == WorkspaceStatus.PendingVerification)
            {
                workspace.BeginProvisioningAfterOwnerVerification();

                List<WorkspaceModuleProvisioning> pendingModules = WorkspaceModuleNames.All
                    .Select(module => WorkspaceModuleProvisioning.CreatePending(workspace.Id, module))
                    .ToList();
                await provisioningRepo.AddRangeAsync(pendingModules, cancellationToken);
            }
            else if (!workspace.AllowsSignIn())
            {
                return Result.Failure<VerifyEmailSuccessDto>(
                    ErrorCodes.BusinessRule,
                    "Workspace is not ready for sign-in.");
            }

            activeWorkspaces.Add((membership, workspace));
        }

        if (activeWorkspaces.Count == 0)
        {
            return Result.Failure<VerifyEmailSuccessDto>(
                ErrorCodes.BusinessRule,
                "The account does not have a workspace.");
        }

        (WorkspaceMembership primaryMembership, Workspace primaryWorkspace) =
            SelectPrimaryWorkspace(activeWorkspaces);

        Role? adminRole = await roleRepo.GetByNameAsync("Admin", primaryWorkspace.Id, cancellationToken);
        if (adminRole is not null && !primaryMembership.RoleIds.Contains(adminRole.Id))
            primaryMembership.AssignRole(adminRole.Id);

        user.VerifyEmail();

        // Gather the sign-in claims here so the endpoint can establish the session
        // from a single command result — no second round-trip that could fail after
        // the one-time link has already been consumed. Read before commit so any
        // failure leaves the verification token unused.
        IReadOnlyList<Role> roles =
            await roleRepo.GetByIdsAsync(primaryMembership.RoleIds, primaryWorkspace.Id, cancellationToken);
        List<string> permissions = roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new VerifyEmailSuccessDto(
            user.Id,
            primaryWorkspace.Id,
            user.Email.Value,
            user.FullName,
            permissions,
            primaryWorkspace.Status == WorkspaceStatus.Active
                ? VerifyEmailNextStep.Dashboard
                : VerifyEmailNextStep.WorkspaceProvisioning));
    }

    private static (WorkspaceMembership Membership, Workspace Workspace) SelectPrimaryWorkspace(
        IReadOnlyList<(WorkspaceMembership Membership, Workspace Workspace)> workspaces)
    {
        return workspaces
            .OrderBy(item => item.Workspace.Type == WorkspaceType.Team ? 0 : 1)
            .ThenBy(item => item.Membership.CreatedAt)
            .First();
    }
}
