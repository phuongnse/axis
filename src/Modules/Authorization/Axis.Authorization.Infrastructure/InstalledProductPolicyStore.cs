using System.Text.Json;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure;

internal sealed class InstalledProductPolicyStore(AuthorizationDbContext context)
    : IInstalledProductPolicyStore
{
    public async Task<StoredProductPolicy?> GetAsync(
        Guid workspaceId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        InstalledPolicyRow? row = await context.Policies.SingleOrDefaultAsync(
            value => value.WorkspaceId == workspaceId && value.VersionId == versionId,
            cancellationToken);
        if (row is null)
            return null;

        try
        {
            ProductPolicyComponent? component = JsonSerializer.Deserialize<ProductPolicyComponent>(
                row.CanonicalContent,
                ProductPolicyJson.Options);
            return component is null
                ? null
                : new(row.WorkspaceId, component, row.CanonicalContent, row.Provenance, row.InstalledAt);
        }
        catch (JsonException)
        {
            throw new AuthorizationPersistenceConflictException(
                "The stored product policy is not readable.");
        }
    }

    public Task AddAsync(
        StoredProductPolicy policy,
        CancellationToken cancellationToken = default) =>
        context.Policies.AddAsync(new InstalledPolicyRow
        {
            WorkspaceId = policy.WorkspaceId,
            VersionId = policy.Component.VersionId,
            PolicyKey = policy.Component.PolicyKey,
            CanonicalContent = policy.CanonicalContent,
            Provenance = policy.Provenance,
            InstalledAt = policy.InstalledAt,
        }, cancellationToken).AsTask();

    public async Task<bool> TryUpdateProvenanceAsync(
        Guid workspaceId,
        Guid versionId,
        string expectedProvenance,
        string newProvenance,
        CancellationToken cancellationToken = default) =>
        await context.Policies
            .Where(value => value.WorkspaceId == workspaceId &&
                value.VersionId == versionId &&
                value.Provenance == expectedProvenance)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.Provenance, newProvenance),
                cancellationToken) == 1;

    public async Task<IReadOnlyList<StoredProductPolicy>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        List<InstalledPolicyRow> rows = await context.Policies.AsNoTracking()
            .Where(value => value.WorkspaceId == workspaceId)
            .OrderBy(value => value.PolicyKey)
            .ThenBy(value => value.VersionId)
            .ToListAsync(cancellationToken);
        List<StoredProductPolicy> result = [];
        foreach (InstalledPolicyRow row in rows)
        {
            ProductPolicyComponent? component = JsonSerializer.Deserialize<ProductPolicyComponent>(
                row.CanonicalContent,
                ProductPolicyJson.Options);
            if (component is null)
                throw new AuthorizationPersistenceConflictException(
                    "The stored product policy is not readable.");
            result.Add(new(
                row.WorkspaceId,
                component,
                row.CanonicalContent,
                row.Provenance,
                row.InstalledAt));
        }
        return result;
    }
}
