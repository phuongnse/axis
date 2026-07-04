using Axis.Identity.Domain.ValueObjects;
using FluentValidation;

namespace Axis.Identity.Application.Commands.UpdateUserThemePreference;

public sealed class UpdateUserThemePreferenceCommandValidator
    : AbstractValidator<UpdateUserThemePreferenceCommand>
{
    public UpdateUserThemePreferenceCommandValidator()
    {
        RuleFor(command => command.Theme)
            .NotEmpty().WithMessage("Theme is required.")
            .Must(UserTheme.IsSupported)
            .WithMessage("Theme is not supported.");
    }
}
