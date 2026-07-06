using Axis.Identity.Application;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using FluentValidation;

namespace Axis.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .WithErrorCode(IdentityProblemCodes.RegisterFullNameRequired)
            .MaximumLength(200)
            .WithMessage("Full name must be 200 characters or fewer.")
            .WithErrorCode(IdentityProblemCodes.RegisterFullNameTooLong);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .WithErrorCode(IdentityProblemCodes.RegisterEmailRequired)
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .WithErrorCode(IdentityProblemCodes.RegisterEmailInvalid);

        RuleFor(x => x.AcceptedTermsVersion)
            .Equal(WellKnownLegalDocuments.TermsVersion)
            .WithMessage("You must accept the current Terms of Service.")
            .WithErrorCode(IdentityProblemCodes.RegisterTermsCurrentRequired);

        RuleFor(x => x.AcceptedPrivacyVersion)
            .Equal(WellKnownLegalDocuments.PrivacyVersion)
            .WithMessage("You must accept the current Privacy Policy.")
            .WithErrorCode(IdentityProblemCodes.RegisterPrivacyCurrentRequired);

        RuleFor(x => x.PreferredLanguage)
            .Must(language => string.IsNullOrWhiteSpace(language) || UserLanguage.IsSupported(language))
            .WithMessage("Language is not supported.")
            .WithErrorCode(IdentityProblemCodes.RegisterPreferredLanguageUnsupported);

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PasswordPolicy.RequiredMessage)
            .WithErrorCode(IdentityProblemCodes.RegisterPasswordRequired)
            .Must((command, password) =>
                PasswordPolicy.Validate(password, command.Email, command.FullName) is null)
            .WithMessage((command, password) =>
                PasswordPolicy.Validate(password, command.Email, command.FullName)!)
            .WithErrorCode(IdentityProblemCodes.RegisterPasswordPolicyFailed);

        RuleFor(x => x.PasswordConfirmation)
            .Equal(x => x.Password).WithMessage("Password confirmation must match password.")
            .WithErrorCode(IdentityProblemCodes.RegisterPasswordConfirmationMismatch);
    }
}
