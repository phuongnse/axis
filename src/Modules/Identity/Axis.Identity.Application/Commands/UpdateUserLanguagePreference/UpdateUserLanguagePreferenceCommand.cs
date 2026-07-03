using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateUserLanguagePreference;

public sealed record UpdateUserLanguagePreferenceCommand(Guid UserId, string Language)
    : ICommand<LanguagePreferenceDto>;

public sealed record LanguagePreferenceDto(string Language);
