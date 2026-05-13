using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.AcceptInvitation;

/// <summary>US-018: Validates token, creates user, assigns role, marks invitation accepted.</summary>
public sealed class AcceptInvitationHandler(
    IInvitationRepository invitationRepo,
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IPasswordHasher hasher,
    IUnitOfWork uow)
    : ICommandHandler<AcceptInvitationCommand, AcceptInvitationResult>
{
    public async Task<Result<AcceptInvitationResult>> Handle(
        AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        Invitation? invitation = await invitationRepo.GetByTokenAsync(command.Token, cancellationToken);
        if (invitation is null)
            return Result.Failure<AcceptInvitationResult>(ErrorCodes.NotFound, "Invalid or unknown invitation token.");

        try
        {
            invitation.Accept();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<AcceptInvitationResult>(ErrorCodes.BusinessRule, ex.Message);
        }

        if (await userRepo.EmailExistsPlatformWideAsync(invitation.Email, cancellationToken))
            return Result.Failure<AcceptInvitationResult>(ErrorCodes.Conflict,
                "An account with this email already exists. Please sign in with your existing credentials.");

        string passwordHash = hasher.Hash(command.Password);

        User user = User.Create(command.FirstName, command.LastName, invitation.Email, invitation.OrganizationId);
        user.SetPasswordHash(passwordHash);
        user.AssignRole(invitation.RoleId);

        await userRepo.AddAsync(user, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        IReadOnlyList<Role> roles = await roleRepo.GetByIdsAsync([invitation.RoleId], invitation.OrganizationId, cancellationToken);
        List<string> permissions = roles.SelectMany(r => r.Permissions).Distinct().ToList();

        return new AcceptInvitationResult(
            user.Id,
            invitation.OrganizationId,
            invitation.Email.Value,
            $"{command.FirstName} {command.LastName}",
            permissions);
    }
}
