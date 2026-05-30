using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CreateExternalRegistrationSession;

public record CreateExternalRegistrationSessionCommand(
    ExternalIdentityProvider Provider,
    string ProviderKey,
    string Email,
    string DisplayName) : ICommand<Guid>;
