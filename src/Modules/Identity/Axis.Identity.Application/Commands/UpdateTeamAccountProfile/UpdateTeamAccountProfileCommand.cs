using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateTeamAccountProfile;

public sealed record UpdateTeamAccountProfileCommand(
    Guid TeamAccountId,
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    byte[]? LogoBytes,
    string? LogoContentType) : ICommand;
