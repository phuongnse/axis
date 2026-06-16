using Axis.Identity.Domain.Legal;
using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterWorkspace;

public sealed class RegisterWorkspaceCommandValidator : AbstractValidator<RegisterWorkspaceCommand>
{
    public RegisterWorkspaceCommandValidator()
    {
        RuleFor(x => x.WorkspaceName)
            .NotEmpty().WithMessage("Workspace name is required.")
            .Length(2, 100).WithMessage("Workspace name must be between 2 and 100 characters.");

        RuleFor(x => x.WorkspaceContactEmail)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.AcceptedTermsVersion)
            .Equal(WellKnownLegalDocuments.TermsVersion)
            .WithMessage("You must accept the current Terms of Service.");

        RuleFor(x => x.AcceptedPrivacyVersion)
            .Equal(WellKnownLegalDocuments.PrivacyVersion)
            .WithMessage("You must accept the current Privacy Policy.");
    }
}
