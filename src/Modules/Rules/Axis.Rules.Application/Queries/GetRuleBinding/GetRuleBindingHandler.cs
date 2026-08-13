using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleBinding;

public sealed class GetRuleBindingHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization,
    IRuleBindingRepository repository)
    : IQueryHandler<GetRuleBindingQuery, Result<RuleBindingDto>>
{
    public async Task<Result<RuleBindingDto>> Handle(
        GetRuleBindingQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleBindingDto>();
        if (query.BindingId == Guid.Empty)
            return NotFound();

        RuleBinding? binding = await repository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(query.BindingId), workspaceId, cancellationToken);
        if (binding is null)
            return NotFound();

        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleBindingDto>(decision);

        return RuleBindingContractMapper.ToDto(binding);
    }

    private static Result<RuleBindingDto> NotFound() =>
        Result.Failure<RuleBindingDto>(ErrorCodes.NotFound, "Rule binding was not found.");
}
