using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;

namespace Axis.Rules.Application.Commands.UpdateRuleBinding;

public sealed class UpdateRuleBindingHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository definitionRepository,
    IRuleBindingRepository bindingRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateRuleBindingCommand, RuleBindingDto>
{
    public async Task<Result<RuleBindingDto>> Handle(UpdateRuleBindingCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleBindingDto>();
        if (currentUser.UserId is not Guid userId)
            return RuleDefinitionFailures.MissingUser<RuleBindingDto>();
        if (command.BindingId == Guid.Empty)
            return RuleDefinitionFailures.NotFound<RuleBindingDto>();

        RuleBinding? binding = await bindingRepository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(command.BindingId), workspaceId, cancellationToken);
        if (binding is null)
            return RuleDefinitionFailures.NotFound<RuleBindingDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.Request.DefinitionKey);
        Result<IReadOnlyDictionary<string, RuleInputMapping>> mappings = RuleBindingContractMapper.ToDomain(command.Request.InputMappings);
        if (key.IsFailure || mappings.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(key.IsFailure ? key.Error : mappings.Error);
        bool retargetsVersion = binding.DefinitionKey != key.Value ||
            binding.DefinitionVersion != command.Request.DefinitionVersion;
        Result<RuleDefinitionVersion> version = await CreateRuleBindingHandler.ResolveVersionAsync(
            workspaceId, key.Value, command.Request.DefinitionVersion, definitionRepository, cancellationToken,
            requireActive: retargetsVersion);
        if (version.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleBindingDto>();
        Result valid = RuleBindingValidator.Validate(version.Value, mappings.Value);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(valid.Error);

        Result updated = binding.Update(
            command.Request.ExpectedRevision,
            key.Value,
            command.Request.DefinitionVersion,
            command.Request.TargetType,
            command.Request.TargetId,
            command.Request.UseCaseOrTrigger,
            mappings.Value,
            command.Request.Priority,
            command.Request.Enabled,
            (DomainFailureBehavior)command.Request.FailureBehavior,
            userId,
            DateTime.UtcNow);
        if (updated.IsFailure)
            return updated.ErrorCode == ErrorCodes.Conflict
                ? RuleDefinitionFailures.Conflict<RuleBindingDto>(updated.Error)
                : RuleDefinitionFailures.Invalid<RuleBindingDto>(updated.Error);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return RuleDefinitionFailures.Conflict<RuleBindingDto>("The rule binding has changed.");
        }
        return RuleBindingContractMapper.ToDto(binding);
    }
}
