using System.Text.Json;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class SolutionVersionRepository(SolutionsDbContext context) : ISolutionVersionRepository
{
    public Task<SolutionVersion?> FindByIdentityAsync(string solutionKey, string version, CancellationToken cancellationToken = default) =>
        context.SolutionVersions.SingleOrDefaultAsync(x => x.SolutionKey == solutionKey && x.Version == version, cancellationToken);

    public Task<SolutionVersion?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SolutionVersions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SolutionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.SolutionVersions.AsNoTracking()
            .OrderBy(x => x.SolutionKey)
            .ThenByDescending(x => x.PublishedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SolutionVersion version, IReadOnlyList<VerifiedSolutionComponent> components, CancellationToken cancellationToken = default)
    {
        await context.SolutionVersions.AddAsync(version, cancellationToken);
        await context.Components.AddRangeAsync(components.Select(x => new SolutionComponentRecord
        {
            SolutionVersionId = version.Id,
            Type = x.Type,
            Key = x.Key,
            Sha256 = x.Sha256,
            Content = x.Content,
            DependsOnJson = JsonSerializer.Serialize(x.DependsOn),
        }), cancellationToken);
    }

    public async Task<IReadOnlyList<VerifiedSolutionComponent>> GetComponentsAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        (await context.Components.AsNoTracking().Where(x => x.SolutionVersionId == versionId).OrderBy(x => x.Type).ThenBy(x => x.Key).ToListAsync(cancellationToken))
        .Select(x => new VerifiedSolutionComponent(x.Type, x.Key, x.Sha256, x.Content,
            JsonSerializer.Deserialize<List<SolutionComponentReference>>(x.DependsOnJson) ?? [])).ToArray();

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<VerifiedSolutionComponent>>> GetComponentsAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken = default)
    {
        List<SolutionComponentRecord> rows = await context.Components.AsNoTracking()
            .Where(value => versionIds.Contains(value.SolutionVersionId))
            .OrderBy(value => value.SolutionVersionId)
            .ThenBy(value => value.Type)
            .ThenBy(value => value.Key)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, IReadOnlyList<VerifiedSolutionComponent>> result = rows
            .GroupBy(value => value.SolutionVersionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VerifiedSolutionComponent>)group.Select(value =>
                    new VerifiedSolutionComponent(
                        value.Type,
                        value.Key,
                        value.Sha256,
                        value.Content,
                        JsonSerializer.Deserialize<List<SolutionComponentReference>>(
                            value.DependsOnJson) ?? [])).ToArray());
        foreach (Guid versionId in versionIds)
            result.TryAdd(versionId, []);
        return result;
    }
}
