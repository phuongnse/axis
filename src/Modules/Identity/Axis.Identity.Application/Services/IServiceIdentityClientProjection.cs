using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Services;

public interface IServiceIdentityClientProjection
{
    Task StageAsync(ServiceIdentity identity, CancellationToken ct = default);
}

public sealed class ServiceIdentityClientProjectionException(string message) : Exception(message);
