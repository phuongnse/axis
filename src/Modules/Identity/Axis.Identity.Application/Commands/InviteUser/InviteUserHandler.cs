using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.InviteUser;

public sealed class InviteUserHandler(
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IInvitationRepository invitationRepo,
    IOrganizationRepository orgRepo,
    IEmailSender emailSender,
    IUnitOfWork uow)
    : ICommandHandler<InviteUserCommand>
{
    public async Task Handle(InviteUserCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
            throw new ValidationException(emailResult.Error);

        var email = emailResult.Value;

        // US-017: cannot invite existing member
        var existingMember = await userRepo.GetByEmailAsync(email, command.OrganizationId, cancellationToken);
        if (existingMember is not null)
            throw new ValidationException("This user is already a member.");

        // US-017: cannot invite email with pending invitation
        var existingInvitation = await invitationRepo.GetPendingByEmailAsync(
            email, command.OrganizationId, cancellationToken);
        if (existingInvitation is not null)
            throw new ValidationException("An invitation has already been sent to this address.");

        // Validate role exists in this org
        var role = await roleRepo.GetByIdAsync(command.RoleId, command.OrganizationId, cancellationToken);
        if (role is null)
            throw new ValidationException("The specified role was not found in this organization.");

        var org = await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (org is null)
            throw new ValidationException("Organization not found.");

        var invitation = Invitation.Create(email, command.OrganizationId, command.RoleId, command.InvitedById);
        await invitationRepo.AddAsync(invitation, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        await emailSender.SendInvitationEmailAsync(
            email.Value, org.Name, invitation.Token, cancellationToken);
    }
}
