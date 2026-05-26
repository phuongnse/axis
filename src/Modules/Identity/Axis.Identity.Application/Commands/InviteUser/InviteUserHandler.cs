using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.InviteUser;

public sealed class InviteUserHandler(
    IPlanLimitService planLimitService,
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IInvitationRepository invitationRepo,
    IOrganizationRepository orgRepo,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<InviteUserCommand>
{
    public async Task<Result> Handle(InviteUserCommand command, CancellationToken cancellationToken)
    {
        Result planCheck = await planLimitService.EnsureWithinLimitAsync(
            command.OrganizationId,
            PlanLimitResourceType.Users,
            increment: 1,
            cancellationToken);
        if (planCheck.IsFailure)
            return planCheck;

        Result<Email> emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
            return Result.Failure(ErrorCodes.BusinessRule, emailResult.Error);

        Email email = emailResult.Value;

        // US-017: cannot invite existing member
        User? existingMember = await userRepo.GetByEmailAsync(email, command.OrganizationId, cancellationToken);
        if (existingMember is not null)
            return Result.Failure(ErrorCodes.Conflict, "This user is already a member.");

        // US-017: cannot invite email with pending invitation
        Invitation? existingInvitation = await invitationRepo.GetPendingByEmailAsync(
            email, command.OrganizationId, cancellationToken);
        if (existingInvitation is not null)
            return Result.Failure(ErrorCodes.Conflict, "An invitation has already been sent to this address.");

        // Validate role exists in this org
        Role? role = await roleRepo.GetByIdAsync(command.RoleId, command.OrganizationId, cancellationToken);
        if (role is null)
            return Result.Failure(ErrorCodes.NotFound, "The specified role was not found in this organization.");

        Organization? org = await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (org is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        Invitation invitation = Invitation.Create(email, command.OrganizationId, command.RoleId, command.InvitedById);
        await invitationRepo.AddAsync(invitation, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        await emailSender.SendInvitationEmailAsync(
            email.Value, org.Name, invitation.Token, cancellationToken);

        return Result.Success();
    }
}
