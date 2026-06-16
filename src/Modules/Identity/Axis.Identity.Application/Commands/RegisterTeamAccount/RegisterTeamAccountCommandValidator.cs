using Axis.Identity.Domain.Legal;
using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterTeamAccount;

public sealed class RegisterTeamAccountCommandValidator : AbstractValidator<RegisterTeamAccountCommand>
{
    public RegisterTeamAccountCommandValidator()
    {
        RuleFor(x => x.TeamAccountName)
            .NotEmpty().WithMessage("Team account name is required.")
            .Length(2, 100).WithMessage("Team account name must be between 2 and 100 characters.");

        RuleFor(x => x.TeamContactEmail)
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
