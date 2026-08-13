using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.CreateRuleDefinitionVersion;

public sealed class CreateRuleDefinitionVersionHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IWorkspaceProductBuilderAuthorization authorization,
    IRuleDefinitionRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateRuleDefinitionVersionCommand, RuleDefinitionDetailDto>
{
    public async Task<Result<RuleDefinitionDetailDto>> Handle(
        CreateRuleDefinitionVersionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleDefinitionDetailDto>();
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.DefinitionKey);
        if (key.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        WorkspaceProductBuilderDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization, workspaceId, currentSubject.Subject, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization<RuleDefinitionDetailDto>(decision);

        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(key.Value, workspaceId, cancellationToken);
        if (definition is null)
            return RuleDefinitionFailures.NotFound<RuleDefinitionDetailDto>();

        Result<RuleDefinitionVersion> created = definition.CreateVersion(
            command.ExpectedRevision,
            RuleSubjectReferenceMapper.ToDomain(currentSubject.Subject),
            DateTime.UtcNow);
        if (created.IsFailure)
            return created.ErrorCode == ErrorCodes.Conflict
                ? RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>(created.Error)
                : RuleDefinitionFailures.Invalid<RuleDefinitionDetailDto>(created.Error);

        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (ConcurrencyException) { return RuleDefinitionFailures.Conflict<RuleDefinitionDetailDto>("The rule definition has changed."); }
        return RuleContractMapper.ToDetailDto(definition, canManage: true);
    }
}
