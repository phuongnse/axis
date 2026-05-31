using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetExternalRegistrationSession;

public record GetExternalRegistrationSessionQuery(Guid SessionId)
    : IQuery<ExternalRegistrationSessionDto?>;

public record ExternalRegistrationSessionDto(
    string Email,
    string DisplayName);
