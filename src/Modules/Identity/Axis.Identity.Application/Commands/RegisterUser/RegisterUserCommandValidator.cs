using Axis.Identity.Domain.Legal;
using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.AcceptedTermsVersion)
            .Equal(WellKnownLegalDocuments.TermsVersion)
            .WithMessage("You must accept the current Terms of Service.");

        RuleFor(x => x.AcceptedPrivacyVersion)
            .Equal(WellKnownLegalDocuments.PrivacyVersion)
            .WithMessage("You must accept the current Privacy Policy.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches(@"[a-zA-Z]").WithMessage("Password must contain at least one letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.PasswordConfirmation)
            .Equal(x => x.Password).WithMessage("Password confirmation must match password.");

        RuleFor(x => x.OrganizationSetupToken)
            .MaximumLength(512);
    }
}
