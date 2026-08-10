using System.Text.Json;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure;

internal sealed class ProductAuthorizationReadStore(AuthorizationDbContext context)
    : IProductPolicyReadStore
{
    public async Task<IReadOnlyList<ProductPolicyGrant>> ListActiveGrantsAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from assignment in context.Assignments.AsNoTracking()
            join policy in context.Policies.AsNoTracking()
                on new { assignment.WorkspaceId, VersionId = assignment.PolicyVersionId }
                equals new { policy.WorkspaceId, policy.VersionId }
            where assignment.WorkspaceId == workspaceId
                && assignment.SubjectKind == subject.Kind.ToString()
                && assignment.SubjectId == subject.Id
                && assignment.IsActive
            select new { assignment.RoleKey, policy.CanonicalContent })
            .ToListAsync(cancellationToken);

        List<ProductPolicyGrant> grants = [];
        foreach (var row in rows)
        {
            try
            {
                ProductPolicyComponent? component =
                    JsonSerializer.Deserialize<ProductPolicyComponent>(
                        row.CanonicalContent,
                        ProductPolicyJson.Options);
                if (component is not null)
                {
                    grants.AddRange(component.Grants.Where(grant =>
                        StringComparer.Ordinal.Equals(grant.RoleKey, row.RoleKey)));
                }
            }
            catch (JsonException)
            {
                throw new AuthorizationPersistenceConflictException(
                    "The stored product policy is not readable.");
            }
        }

        return grants;
    }
}
