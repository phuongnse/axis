using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.ListRuleContextSchemas;

public sealed class ListRuleContextSchemasHandler(
    ICurrentUser currentUser,
    RuleContextSchemaRegistry registry)
    : IQueryHandler<ListRuleContextSchemasQuery, Result<IReadOnlyList<RuleContextSchemaDto>>>
{
    public async Task<Result<IReadOnlyList<RuleContextSchemaDto>>> Handle(
        ListRuleContextSchemasQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<IReadOnlyList<RuleContextSchemaDto>>();

        return Result.Success(await registry.ListAsync(
            workspaceId,
            cancellationToken: cancellationToken));
    }
}
