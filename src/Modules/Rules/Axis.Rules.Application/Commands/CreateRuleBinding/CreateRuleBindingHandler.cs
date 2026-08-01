using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;

namespace Axis.Rules.Application.Commands.CreateRuleBinding;

public sealed class CreateRuleBindingHandler(
    ICurrentUser currentUser,
    IRuleDefinitionRepository definitionRepository,
    IRuleBindingRepository bindingRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateRuleBindingCommand, RuleBindingDto>
{
    public async Task<Result<RuleBindingDto>> Handle(
        CreateRuleBindingCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return RuleDefinitionFailures.MissingWorkspace<RuleBindingDto>();
        if (currentUser.UserId is not Guid userId)
            return RuleDefinitionFailures.MissingUser<RuleBindingDto>();

        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(command.Request.DefinitionKey);
        Result<IReadOnlyDictionary<string, RuleInputMapping>> mappings = RuleBindingContractMapper.ToDomain(command.Request.InputMappings);
        if (key.IsFailure || mappings.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(key.IsFailure ? key.Error : mappings.Error);

        Result<RuleDefinitionVersion> version = await ResolveVersionAsync(
            workspaceId,
            key.Value,
            command.Request.DefinitionVersion,
            definitionRepository,
            cancellationToken);
        if (version.IsFailure)
            return RuleDefinitionFailures.NotFound<RuleBindingDto>();

        Result shape = RuleBindingValidator.ValidateRequestShape(command.Request);
        if (shape.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(shape.Error);
        Result valid = RuleBindingValidator.Validate(version.Value, mappings.Value);
        if (valid.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(valid.Error);

        Result<RuleBinding> binding = RuleBinding.Create(
            workspaceId,
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
        if (binding.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleBindingDto>(binding.Error);

        await bindingRepository.AddAsync(binding.Value, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException exception)
        {
            return RuleDefinitionFailures.Conflict<RuleBindingDto>(exception.Message);
        }
        return RuleBindingContractMapper.ToDto(binding.Value);
    }

    internal static async Task<Result<RuleDefinitionVersion>> ResolveVersionAsync(
        Guid workspaceId,
        RuleDefinitionKey key,
        int versionNumber,
        IRuleDefinitionRepository repository,
        CancellationToken cancellationToken)
    {
        RuleDefinition? system = SystemRuleCatalog.Find(key.Value, versionNumber);
        if (system is not null)
            return system.FindVersion(versionNumber)!;
        RuleDefinition? definition = await repository.GetByKeyForWorkspaceAsync(key, workspaceId, cancellationToken);
        if (definition is null || definition.Status == DomainLifecycleStatus.Archived)
            return Result.Failure<RuleDefinitionVersion>(ErrorCodes.NotFound, "Rule definition was not found.");
        RuleDefinitionVersion? version = definition.FindVersion(versionNumber);
        return version is null || versionNumber != definition.LatestPublishedVersion
            ? Result.Failure<RuleDefinitionVersion>(ErrorCodes.NotFound, "Published rule version was not found.")
            : version;
    }
}
