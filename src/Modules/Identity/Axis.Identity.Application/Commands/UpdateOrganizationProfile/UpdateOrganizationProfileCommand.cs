using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateOrganizationProfile;

public sealed record UpdateOrganizationProfileCommand(
    Guid OrganizationId,
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    byte[]? LogoBytes,
    string? LogoContentType) : ICommand;
