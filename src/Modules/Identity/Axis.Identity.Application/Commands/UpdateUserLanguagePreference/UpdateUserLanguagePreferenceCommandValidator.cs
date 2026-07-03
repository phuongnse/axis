using Axis.Identity.Domain.ValueObjects;
using FluentValidation;

namespace Axis.Identity.Application.Commands.UpdateUserLanguagePreference;

public sealed class UpdateUserLanguagePreferenceCommandValidator
    : AbstractValidator<UpdateUserLanguagePreferenceCommand>
{
    public UpdateUserLanguagePreferenceCommandValidator()
    {
        RuleFor(command => command.Language)
            .NotEmpty().WithMessage("Language is required.")
            .Must(UserLanguage.IsSupported)
            .WithMessage("Language is not supported.");
    }
}
