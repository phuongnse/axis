using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.Identity.Application.Commands.AcceptInvitation;

/// <summary>US-018: Validates token, creates user, assigns role, marks invitation accepted.</summary>
public sealed class AcceptInvitationHandler(
    IInvitationRepository invitationRepo,
    IUserRepository userRepo,
    IPasswordHasher hasher,
    IUnitOfWork uow)
    : ICommandHandler<AcceptInvitationCommand>
{
    public async Task Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await invitationRepo.GetByTokenAsync(command.Token, cancellationToken);
        if (invitation is null)
            throw new ValidationException("Invalid or unknown invitation token.");

        // Domain will throw InvalidOperationException for expired/accepted/cancelled —
        // wrap in ValidationException so the API layer handles it consistently.
        try
        {
            invitation.Accept();
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        if (await userRepo.EmailExistsPlatformWideAsync(invitation.Email, cancellationToken))
            throw new ValidationException(
                "An account with this email already exists. Please sign in with your existing credentials.");

        var passwordHash = hasher.Hash(command.Password);

        var user = User.Create(command.FirstName, command.LastName, invitation.Email, invitation.OrganizationId);
        user.SetPasswordHash(passwordHash);
        user.AssignRole(invitation.RoleId);

        await userRepo.AddAsync(user, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
