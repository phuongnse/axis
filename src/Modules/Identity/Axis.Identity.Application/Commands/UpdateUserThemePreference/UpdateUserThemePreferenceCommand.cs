using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateUserThemePreference;

public sealed record UpdateUserThemePreferenceCommand(Guid UserId, string Theme)
    : ICommand<ThemePreferenceDto>;

public sealed record ThemePreferenceDto(string Theme);
