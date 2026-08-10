using System.Text.RegularExpressions;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;

namespace Axis.Rules.Application;

public sealed partial class RuleBindingSolutionInstaller(
    IRuleBindingRepository bindings,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRuleBindingSolutionInstaller
{
    public Task<RuleBindingInstallationResult> ValidateAsync(
        Guid workspaceId,
        RuleBindingSolutionComponent component,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(workspaceId, component).Result);

    public async Task<RuleBindingInstallationResult> InstallAsync(
        Guid workspaceId,
        RuleBindingSolutionComponent component,
        RuleBindingInstallationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        (RuleBindingInstallationResult Result, RuleDefinitionKey? DefinitionKey, IReadOnlyDictionary<string, RuleInputMapping>? Mappings) =
            Validate(workspaceId, component);
        if (!Result.IsSuccess || DefinitionKey is null || Mappings is null || !ValidReceipt(receipt))
            return Result.IsSuccess ? Invalid() : Result;

        RuleBinding? existing = await bindings.GetByIdentityForWorkspaceAsync(
            workspaceId,
            DefinitionKey.Value,
            component.DefinitionVersion,
            component.TargetType,
            component.TargetId,
            component.UseCaseOrTrigger,
            cancellationToken);

        if (existing is null)
        {
            Result<RuleBinding> created = RuleBinding.Create(
                workspaceId,
                DefinitionKey.Value,
                component.DefinitionVersion,
                component.TargetType,
                component.TargetId,
                component.UseCaseOrTrigger,
                Mappings,
                component.Priority,
                component.Enabled,
                (DomainFailureBehavior)component.FailureBehavior,
                RuleSubjectReferenceMapper.ToDomain(receipt.Actor),
                clock.GetUtcNow().UtcDateTime);
            if (created.IsFailure)
                return Invalid();

            Result stamped = Stamp(created.Value, component.ComponentKey, receipt);
            if (stamped.IsFailure)
                return Invalid();

            await bindings.AddAsync(created.Value, cancellationToken);
        }
        else
        {
            if (!existing.IsInstalled || !ContentMatches(existing, component, Mappings))
                return new(false, "rules.binding_install_conflict");

            Result advanced = Stamp(existing, component.ComponentKey, receipt);
            if (advanced.IsFailure)
            {
                return advanced.Error.Contains("stale", StringComparison.OrdinalIgnoreCase)
                    ? new(false, "rules.binding_install_stale_receipt")
                    : new(false, "rules.binding_install_conflict");
            }
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new(true);
        }
        catch (ConcurrencyException)
        {
            return new(false, "rules.binding_install_conflict");
        }
        catch (UniqueConstraintException)
        {
            return new(false, "rules.binding_install_conflict");
        }
    }

    public async Task<RuleBindingInstallationReadBack?> ReadBackAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || !ValidComponentKey(componentKey))
            return null;

        RuleBinding? binding = await bindings.GetInstalledByComponentKeyAsync(
            workspaceId,
            componentKey,
            cancellationToken);
        return binding is not { IsInstalled: true } ||
               binding.InstalledSolutionVersionId is not Guid solutionVersionId ||
               binding.InstalledComponentHash is not string componentHash ||
               binding.InstalledOperationId is not Guid operationId ||
               binding.InstalledStepId is not Guid stepId ||
               binding.InstalledLeaseEpoch is not long leaseEpoch
            ? null
            : new(
                binding.WorkspaceId,
                binding.Id.Value,
                binding.Revision,
                componentKey,
                RuleBindingContractMapper.ToSolutionComponent(binding),
                solutionVersionId,
                componentHash,
                operationId,
                stepId,
                leaseEpoch);
    }

    private static (RuleBindingInstallationResult Result, RuleDefinitionKey? DefinitionKey, IReadOnlyDictionary<string, RuleInputMapping>? Mappings)
        Validate(Guid workspaceId, RuleBindingSolutionComponent? component)
    {
        if (workspaceId == Guid.Empty || component is null || !ValidComponentKey(component.ComponentKey) ||
            !StringComparer.Ordinal.Equals(component.TargetType, "business-object-field") ||
            !SemanticPathPattern().IsMatch(component.TargetId) ||
            !TokenPattern().IsMatch(component.UseCaseOrTrigger) ||
            !StringComparer.Ordinal.Equals(
                component.ComponentKey,
                $"{component.DefinitionKey}@{component.DefinitionVersion}:{component.TargetType}:{component.TargetId}:{component.UseCaseOrTrigger}"))
            return (Invalid(), null, null);

        Result<RuleDefinitionKey> definitionKey = RuleDefinitionKey.Create(component.DefinitionKey);
        Result<IReadOnlyDictionary<string, RuleInputMapping>> mappings =
            RuleBindingContractMapper.ToDomain(component.InputMappings);
        if (definitionKey.IsFailure || mappings.IsFailure ||
            !Enum.IsDefined(component.FailureBehavior))
            return (Invalid(), null, null);

        RuleDefinition? definition = BuiltInRuleCatalog.Find(
            definitionKey.Value.Value,
            component.DefinitionVersion);
        RuleDefinitionVersion? version = definition?.FindVersion(component.DefinitionVersion);
        if (version is null || RuleBindingValidator.Validate(version, mappings.Value).IsFailure)
            return (Invalid(), null, null);

        Result candidate = RuleBinding.ValidateInstallationCandidate(
            component.DefinitionVersion,
            component.TargetType,
            component.TargetId,
            component.UseCaseOrTrigger,
            mappings.Value,
            component.Priority,
            (DomainFailureBehavior)component.FailureBehavior);
        return candidate.IsFailure
            ? (Invalid(), null, null)
            : (new(true), definitionKey.Value, mappings.Value);
    }

    private static bool ContentMatches(
        RuleBinding binding,
        RuleBindingSolutionComponent component,
        IReadOnlyDictionary<string, RuleInputMapping> mappings) =>
        binding.DefinitionKey.Value == component.DefinitionKey &&
        binding.DefinitionVersion == component.DefinitionVersion &&
        binding.TargetType == component.TargetType &&
        binding.TargetId == component.TargetId &&
        binding.UseCaseOrTrigger == component.UseCaseOrTrigger &&
        binding.Priority == component.Priority &&
        binding.Enabled == component.Enabled &&
        binding.FailureBehavior == (DomainFailureBehavior)component.FailureBehavior &&
        DomainMappingsEqual(binding.InputMappings, mappings);

    private static bool DomainMappingsEqual(
        IReadOnlyDictionary<string, RuleInputMapping> left,
        IReadOnlyDictionary<string, RuleInputMapping> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out RuleInputMapping? candidate) &&
            pair.Value.Kind == candidate.Kind &&
            StringComparer.Ordinal.Equals(pair.Value.ContextKey, candidate.ContextKey) &&
            pair.Value.LiteralValues.SequenceEqual(candidate.LiteralValues, StringComparer.Ordinal));

    private static Result Stamp(
        RuleBinding binding,
        string componentKey,
        RuleBindingInstallationReceipt receipt) =>
        binding.AdvanceInstallationReceipt(
            receipt.SolutionVersionId,
            componentKey,
            receipt.ComponentHash,
            receipt.OperationId,
            receipt.StepId,
            receipt.LeaseEpoch);

    private static bool ValidReceipt(RuleBindingInstallationReceipt receipt) =>
        receipt is not null && receipt.SolutionVersionId != Guid.Empty &&
        receipt.Actor.Id != Guid.Empty && Enum.IsDefined(receipt.Actor.Kind) &&
        receipt.OperationId != Guid.Empty && receipt.StepId != Guid.Empty &&
        receipt.LeaseEpoch > 0 && IsSha256(receipt.ComponentHash);

    private static bool ValidComponentKey(string? value) =>
        value is { Length: > 0 and <= 200 } && ComponentKeyPattern().IsMatch(value);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit) &&
        StringComparer.Ordinal.Equals(value, value.ToLowerInvariant());

    private static RuleBindingInstallationResult Invalid() =>
        new(false, "rules.binding_component_invalid");

    [GeneratedRegex("^[a-z][a-z0-9_.:@-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentKeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*(\\.[a-z][a-z0-9_-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticPathPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
