using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetEnabledExternalProviders;

public sealed class GetEnabledExternalProvidersHandler(IExternalAuthProviderRegistry registry)
    : IQueryHandler<GetEnabledExternalProvidersQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(
        GetEnabledExternalProvidersQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(registry.GetEnabledProviders());
}
