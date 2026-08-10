using Axis.Solutions.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Solutions.Infrastructure.Services;

/// <summary>Host-independent unit of durable work; a host scheduler supplies operation IDs.</summary>
public sealed class SolutionOperationWorker(IServiceScopeFactory scopes)
{
    public async Task<IReadOnlyList<Guid>> ListRunnableAsync(
        DateTimeOffset now,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ISolutionOperationRepository operations = scope.ServiceProvider
            .GetRequiredService<ISolutionOperationRepository>();
        return await operations.ListRunnableIdsAsync(
            now,
            maximumCount,
            cancellationToken);
    }

    public async Task ProcessAsync(Guid operationId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        SolutionOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<SolutionOrchestrator>();
        await orchestrator.RunOnceAsync(operationId, leaseDuration, cancellationToken);
    }
}
