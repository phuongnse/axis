using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentValidation;

namespace Axis.Identity.Application.Commands.InviteWorkspaceMember;

public sealed class InviteWorkspaceMemberCommandValidator
    : AbstractValidator<InviteWorkspaceMemberCommand>
{
    public InviteWorkspaceMemberCommandValidator()
    {
        RuleFor(command => command.Email)
            .Must(email => Email.Create(email).IsSuccess)
            .WithMessage("A valid recipient email is required.")
            .WithErrorCode(IdentityProblemCodes.InvitationEmailInvalid);

        RuleFor(command => command.RequestedRole)
            .Must(role => Enum.TryParse(role, ignoreCase: true, out WorkspaceMembershipRole parsed)
                && parsed is WorkspaceMembershipRole.Administrator or WorkspaceMembershipRole.Member)
            .WithMessage("The invitation role must be Workspace administrator or Workspace member.")
            .WithErrorCode(IdentityProblemCodes.InvitationRoleUnsupported);
    }
}
