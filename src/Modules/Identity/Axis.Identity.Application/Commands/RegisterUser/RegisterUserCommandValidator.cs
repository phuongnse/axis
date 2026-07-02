using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Legal;
using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200);

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
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PasswordPolicy.RequiredMessage)
            .Must((command, password) =>
                PasswordPolicy.Validate(password, command.Email, command.FullName) is null)
            .WithMessage((command, password) =>
                PasswordPolicy.Validate(password, command.Email, command.FullName)!);

        RuleFor(x => x.PasswordConfirmation)
            .Equal(x => x.Password).WithMessage("Password confirmation must match password.");
    }
}
