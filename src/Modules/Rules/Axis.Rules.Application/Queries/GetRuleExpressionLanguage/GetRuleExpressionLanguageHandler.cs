using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Queries.GetRuleExpressionLanguage;

public sealed class GetRuleExpressionLanguageHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization)
    : IQueryHandler<GetRuleExpressionLanguageQuery, Result<RuleExpressionLanguageDto>>
{
    public async Task<Result<RuleExpressionLanguageDto>> Handle(
        GetRuleExpressionLanguageQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleExpressionLanguageDto>();
        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        return decision.IsAllowed
            ? Result.Success(RuleContractMapper.ToExpressionLanguageDto())
            : RuleDefinitionFailures.Authorization<RuleExpressionLanguageDto>(decision);
    }
}
