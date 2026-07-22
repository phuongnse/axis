using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleDefinition;

public sealed class GetRuleDefinitionHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository repository)
    : IQueryHandler<GetRuleDefinitionQuery, Result<RuleDefinitionDetailDto>>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        GetRuleDefinitionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(query.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        SystemRuleDefinition? system = SystemRuleCatalog.Definitions
            .Where(definition => definition.Key == key.Value)
            .OrderByDescending(definition => definition.Version)
            .FirstOrDefault();
        if (system is not null)
            return RuleContractMapper.ToDetailDto(system);

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(
            key.Value,
            workspaceId,
            cancellationToken);
        return definition is null
            ? RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>()
            : RuleContractMapper.ToDetailDto(definition);
    }
}
