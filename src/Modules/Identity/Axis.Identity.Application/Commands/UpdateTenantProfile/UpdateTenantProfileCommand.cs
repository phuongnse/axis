using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateTenantProfile;

public sealed record UpdateTenantProfileCommand(
    Guid tenantId,
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    byte[]? LogoBytes,
    string? LogoContentType) : ICommand;
