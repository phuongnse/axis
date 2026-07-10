using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using ContractScope = Axis.Rules.Contracts.RuleScope;

namespace Axis.Rules.Application;

public sealed class RuleContextSchemaRegistry(IEnumerable<IRuleContextSchemaProvider> providers)
{
    private readonly IReadOnlyList<IRuleContextSchemaProvider> _providers = providers.ToArray();

    public async Task<IReadOnlyList<RuleContextSchemaDto>> ListAsync(
        Guid workspaceId,
        ContractScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        List<RuleContextSchemaDto> schemas = [];
        foreach (IRuleContextSchemaProvider provider in _providers)
        {
            schemas.AddRange(await provider.ListSchemasAsync(
                workspaceId,
                scope,
                cancellationToken));
        }

        EnsureUnique(schemas);
        return schemas
            .OrderBy(schema => schema.ContextKey, StringComparer.Ordinal)
            .ThenBy(schema => schema.Version)
            .ToArray();
    }

    public async Task<RuleContextSchema?> FindAsync(
        Guid workspaceId,
        string key,
        int version,
        CancellationToken cancellationToken = default)
    {
        List<RuleContextSchemaDto> matches = [];
        foreach (IRuleContextSchemaProvider provider in _providers)
        {
            RuleContextSchemaDto? match = await provider.FindSchemaAsync(
                workspaceId,
                key,
                version,
                cancellationToken);
            if (match is not null)
                matches.Add(match);
        }

        EnsureUnique(matches);
        if (matches.Count == 0)
            return null;

        Axis.Shared.Domain.Primitives.Result<RuleContextSchema> schema =
            RuleContractMapper.ToDomain(matches[0]);
        return schema.IsSuccess
            ? schema.Value
            : throw new InvalidOperationException(schema.Error);
    }

    private static void EnsureUnique(IReadOnlyList<RuleContextSchemaDto> schemas)
    {
        if (schemas.Select(schema => (schema.ContextKey, schema.Version)).Distinct().Count() != schemas.Count)
            throw new InvalidOperationException("Rule context schema keys and versions must be unique.");
    }
}
