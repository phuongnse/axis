using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterOrganization;

public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public RegisterOrganizationCommandValidator()
    {
        RuleFor(x => x.OrgName)
            .NotEmpty().WithMessage("Organization name is required.")
            .Length(2, 100).WithMessage("Organization name must be between 2 and 100 characters.");

        RuleFor(x => x.AdminFirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50);

        RuleFor(x => x.AdminLastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50);

        RuleFor(x => x.AdminEmail)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        When(x => x.ExternalRegistrationSessionId is null, () =>
        {
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .Matches(@"[a-zA-Z]").WithMessage("Password must contain at least one letter.")
                .Matches(@"\d").WithMessage("Password must contain at least one number.");

            RuleFor(x => x.PasswordConfirmation)
                .Equal(x => x.Password).WithMessage("Password confirmation must match password.");
        });

        When(x => x.ExternalRegistrationSessionId is not null, () =>
        {
            RuleFor(x => x.AcceptedTermsVersion)
                .NotEmpty().WithMessage("Terms of Service acceptance is required.");

            RuleFor(x => x.AcceptedPrivacyVersion)
                .NotEmpty().WithMessage("Privacy Policy acceptance is required.");
        });
    }
}
