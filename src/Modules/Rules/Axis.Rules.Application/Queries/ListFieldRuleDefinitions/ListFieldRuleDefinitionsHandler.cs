using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;

namespace Axis.Rules.Application.Queries.ListFieldRuleDefinitions;

public sealed class ListFieldRuleDefinitionsHandler(IFieldRuleDefinitionProvider provider)
    : IQueryHandler<ListFieldRuleDefinitionsQuery, IReadOnlyList<FieldRuleDefinitionDto>>
{
    public Task<IReadOnlyList<FieldRuleDefinitionDto>> Handle(
        ListFieldRuleDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(provider.ListFieldRuleDefinitions());
    }
}
