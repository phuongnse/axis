using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetEnabledExternalProviders;

public record GetEnabledExternalProvidersQuery : IQuery<IReadOnlyList<string>>;
