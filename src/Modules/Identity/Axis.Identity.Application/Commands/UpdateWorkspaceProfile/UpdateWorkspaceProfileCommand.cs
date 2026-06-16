using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateWorkspaceProfile;

public sealed record UpdateWorkspaceProfileCommand(
    Guid workspaceId,
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    byte[]? LogoBytes,
    string? LogoContentType) : ICommand;
