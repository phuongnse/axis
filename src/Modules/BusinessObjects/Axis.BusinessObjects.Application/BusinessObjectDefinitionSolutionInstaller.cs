using System.Text;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Contracts;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using DomainChoiceSelectionMode = Axis.BusinessObjects.Domain.Aggregates.BusinessObjectChoiceSelectionMode;
using DomainFieldType = Axis.BusinessObjects.Domain.Aggregates.BusinessObjectFieldType;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.BusinessObjects.Application;

public sealed class BusinessObjectDefinitionSolutionInstaller(
    IBusinessObjectDefinitionRepository definitions,
    IRuleBindingSolutionInstaller ruleBindings,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IBusinessObjectDefinitionSolutionInstaller
{
    public Task<BusinessObjectDefinitionInstallationResult> ValidateAsync(
        Guid workspaceId,
        BusinessObjectDefinitionSolutionComponent component,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(workspaceId, component).Result);

    public async Task<BusinessObjectDefinitionInstallationResult> InstallAsync(
        Guid workspaceId,
        BusinessObjectDefinitionSolutionComponent component,
        BusinessObjectDefinitionInstallationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        (BusinessObjectDefinitionInstallationResult Result, BusinessObjectDefinitionKey? ObjectKey) =
            Validate(workspaceId, component);
        if (!Result.IsSuccess || ObjectKey is null || !ValidReceipt(receipt))
            return Result.IsSuccess ? Invalid() : Result;

        IReadOnlyList<BusinessObjectFieldDefinitionSpec>? fields = await ResolveFieldsAsync(
            workspaceId,
            component,
            receipt,
            cancellationToken);
        if (fields is null)
            return new(false, "businessObjects.definition_binding_unavailable");

        BusinessObjectDefinition? definition = await definitions.GetByKeyForWorkspaceAsync(
            ObjectKey,
            workspaceId,
            cancellationToken);
        if (definition is null)
        {
            DateTime now = clock.GetUtcNow().UtcDateTime;
            Result<BusinessObjectDefinition> created = BusinessObjectDefinition.CreateUnpublished(
                workspaceId,
                component.Name,
                ObjectKey,
                now);
            if (created.IsFailure ||
                created.Value.SaveUnpublished(
                    component.Name,
                    fields,
                    expectedRevision: 1,
                    updatedAt: now).IsFailure ||
                created.Value.Publish(
                    expectedRevision: 2,
                    publishedBySubject: SubjectReferenceMapper.ToDomain(receipt.Actor),
                    publishedAt: now).IsFailure ||
                Stamp(created.Value, component.ComponentKey, receipt).IsFailure)
            {
                return Invalid();
            }

            definition = created.Value;
            await definitions.AddAsync(definition, cancellationToken);
        }
        else
        {
            if (!definition.IsInstalled ||
                !ContentMatches(definition, component, fields, receipt.Actor))
            {
                return Conflict();
            }

            Result advanced = Stamp(definition, component.ComponentKey, receipt);
            if (advanced.IsFailure)
            {
                return advanced.Error.Contains("stale", StringComparison.OrdinalIgnoreCase)
                    ? new(false, "businessObjects.definition_install_stale_receipt")
                    : Conflict();
            }
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new(true);
        }
        catch (ConcurrencyException)
        {
            return Conflict();
        }
        catch (UniqueConstraintException)
        {
            return Conflict();
        }
    }

    public async Task<BusinessObjectDefinitionInstallationReadBack?> ReadBackAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || !ValidComponentKey(componentKey))
            return null;

        BusinessObjectDefinition? definition = await definitions.GetInstalledByComponentKeyAsync(
            workspaceId,
            componentKey,
            cancellationToken);
        BusinessObjectDefinitionVersion? version = definition?.Versions
            .SingleOrDefault(candidate =>
                candidate.VersionNumber == definition.LatestPublishedVersionNumber);
        BusinessObjectDefinitionSolutionComponent? component = definition is null
            ? null
            : ToComponent(definition);
        return definition is not { IsInstalled: true } || version is null || component is null ||
               definition.InstalledSolutionVersionId is not Guid solutionVersionId ||
               definition.InstalledComponentHash is not string componentHash ||
               definition.InstalledOperationId is not Guid operationId ||
               definition.InstalledStepId is not Guid stepId ||
               definition.InstalledLeaseEpoch is not long leaseEpoch
            ? null
            : new(
                definition.WorkspaceId,
                definition.Id.Value,
                version.Id.Value,
                componentKey,
                component,
                solutionVersionId,
                componentHash,
                operationId,
                stepId,
                leaseEpoch);
    }

    private async Task<IReadOnlyList<BusinessObjectFieldDefinitionSpec>?> ResolveFieldsAsync(
        Guid workspaceId,
        BusinessObjectDefinitionSolutionComponent component,
        BusinessObjectDefinitionInstallationReceipt receipt,
        CancellationToken cancellationToken)
    {
        Dictionary<string, RuleBindingInstallationReadBack> resolved = new(StringComparer.Ordinal);
        List<BusinessObjectFieldDefinitionSpec> fields = [];
        foreach (BusinessObjectDefinitionSolutionField field in component.Fields)
        {
            List<BusinessObjectFieldRuleSpec> rules = [];
            foreach (string bindingKey in field.BindingKeys)
            {
                if (!resolved.TryGetValue(bindingKey, out RuleBindingInstallationReadBack? binding))
                {
                    binding = await ruleBindings.ReadBackAsync(
                        workspaceId,
                        bindingKey,
                        cancellationToken);
                    if (binding is null || binding.WorkspaceId != workspaceId ||
                        binding.SolutionVersionId != receipt.SolutionVersionId ||
                        binding.OperationId != receipt.OperationId ||
                        binding.LeaseEpoch > receipt.LeaseEpoch ||
                        !StringComparer.Ordinal.Equals(binding.ComponentKey, bindingKey) ||
                        !StringComparer.Ordinal.Equals(binding.Component.TargetType, "business-object-field") ||
                        !StringComparer.Ordinal.Equals(
                            binding.Component.TargetId,
                            $"{component.ObjectKey}.{field.FieldKey}"))
                    {
                        return null;
                    }
                    resolved.Add(bindingKey, binding);
                }
                rules.Add(new BusinessObjectFieldRuleSpec(
                    binding.BindingId,
                    BindingRevision: binding.BindingRevision,
                    BindingKey: bindingKey));
            }

            BusinessObjectChoiceFieldConfigurationSpec? choice = field.ChoiceConfiguration is null
                ? null
                : new(
                    (DomainChoiceSelectionMode)field.ChoiceConfiguration.SelectionMode,
                    field.ChoiceConfiguration.Options
                        .Select(option => new BusinessObjectChoiceOptionSpec(
                            option.OptionKey,
                            option.Label,
                            option.Order))
                        .ToArray());
            fields.Add(new BusinessObjectFieldDefinitionSpec(
                field.FieldKey,
                field.Label,
                field.Order,
                (DomainFieldType)field.FieldType,
                rules,
                choice));
        }
        return fields;
    }

    private static (BusinessObjectDefinitionInstallationResult Result, BusinessObjectDefinitionKey? ObjectKey)
        Validate(Guid workspaceId, BusinessObjectDefinitionSolutionComponent? component)
    {
        if (workspaceId == Guid.Empty || component is null ||
            !StringComparer.Ordinal.Equals(component.ComponentKey, component.ObjectKey) ||
            !ValidComponentKey(component.ComponentKey) || !ValidText(component.Name) ||
            component.Fields is null || component.Fields.Count == 0)
        {
            return (Invalid(), null);
        }

        Result<BusinessObjectDefinitionKey> objectKey = BusinessObjectDefinitionKey.Create(component.ObjectKey);
        if (objectKey.IsFailure || !ValidFields(component.Fields))
            return (Invalid(), null);

        return (new(true), objectKey.Value);
    }

    private static bool ValidFields(IReadOnlyList<BusinessObjectDefinitionSolutionField> fields)
    {
        HashSet<string> fieldKeys = new(StringComparer.Ordinal);
        for (int index = 0; index < fields.Count; index++)
        {
            BusinessObjectDefinitionSolutionField field = fields[index];
            if (field is null || field.Order != index ||
                BusinessObjectFieldKey.Create(field.FieldKey).IsFailure ||
                !fieldKeys.Add(field.FieldKey) || !ValidText(field.Label) ||
                !Enum.IsDefined(field.FieldType) || field.BindingKeys is null ||
                !StrictlySortedUnique(field.BindingKeys) ||
                field.BindingKeys.Any(bindingKey => !ValidComponentKey(bindingKey)))
            {
                return false;
            }

            if (field.FieldType == BusinessObjectSolutionFieldType.Choice)
            {
                if (!ValidChoice(field.ChoiceConfiguration))
                    return false;
            }
            else if (field.ChoiceConfiguration is not null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidChoice(BusinessObjectSolutionChoiceConfiguration? choice)
    {
        if (choice is null || !Enum.IsDefined(choice.SelectionMode) ||
            choice.Options is null || choice.Options.Count == 0)
            return false;

        HashSet<string> keys = new(StringComparer.Ordinal);
        for (int index = 0; index < choice.Options.Count; index++)
        {
            BusinessObjectSolutionChoiceOption option = choice.Options[index];
            if (option is null || option.Order != index ||
                BusinessObjectChoiceOptionKey.Create(option.OptionKey).IsFailure ||
                !keys.Add(option.OptionKey) || !ValidText(option.Label))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ContentMatches(
        BusinessObjectDefinition definition,
        BusinessObjectDefinitionSolutionComponent component,
        IReadOnlyList<BusinessObjectFieldDefinitionSpec> fields,
        IdentitySubjectReference actor)
    {
        BusinessObjectDefinitionVersion? version = definition.Versions
            .SingleOrDefault(candidate =>
                candidate.VersionNumber == definition.LatestPublishedVersionNumber);
        return definition.Status == BusinessObjectDefinitionStatus.Published &&
            StringComparer.Ordinal.Equals(definition.Key.Value, component.ObjectKey) &&
            StringComparer.Ordinal.Equals(definition.Name, component.Name) &&
            version is not null && version.PublishedBySubject == SubjectReferenceMapper.ToDomain(actor) &&
            definition.Fields.Count == fields.Count &&
            definition.Fields.OrderBy(field => field.Order).Zip(fields).All(pair => FieldMatches(pair.First, pair.Second));
    }

    private static bool FieldMatches(
        BusinessObjectFieldDefinition field,
        BusinessObjectFieldDefinitionSpec spec) =>
        StringComparer.Ordinal.Equals(field.Key.Value, spec.FieldKey) &&
        StringComparer.Ordinal.Equals(field.Label, spec.Label) &&
        field.Order == spec.Order && field.FieldType == spec.FieldType &&
        field.ChoiceSelectionMode == spec.ChoiceConfiguration?.SelectionMode &&
        ChoiceOptionsMatch(field.ChoiceOptions, spec.ChoiceConfiguration?.Options ?? []) &&
        field.Rules.Select(rule => rule.BindingId)
            .SequenceEqual((spec.Rules ?? []).Select(rule => rule.BindingId)) &&
        field.Rules.Select(rule => rule.BindingRevision)
            .SequenceEqual((spec.Rules ?? []).Select(rule => rule.BindingRevision)) &&
        field.Rules.Select(rule => rule.BindingKey)
            .SequenceEqual((spec.Rules ?? []).Select(rule => rule.BindingKey), StringComparer.Ordinal);

    private static BusinessObjectDefinitionSolutionComponent? ToComponent(
        BusinessObjectDefinition definition)
    {
        List<BusinessObjectDefinitionSolutionField> fields = [];
        foreach (BusinessObjectFieldDefinition field in definition.Fields.OrderBy(field => field.Order))
        {
            string?[] bindingKeys = field.Rules
                .OrderBy(rule => rule.Order)
                .Select(rule => rule.BindingKey)
                .ToArray();
            if (bindingKeys.Any(key => key is null))
                return null;

            BusinessObjectSolutionChoiceConfiguration? choice = field.ChoiceSelectionMode is null
                ? null
                : new(
                    (BusinessObjectSolutionChoiceSelectionMode)field.ChoiceSelectionMode.Value,
                    field.ChoiceOptions
                        .OrderBy(option => option.Order)
                        .Select(option => new BusinessObjectSolutionChoiceOption(
                            option.Key.Value,
                            option.Label,
                            option.Order))
                        .ToArray());
            fields.Add(new(
                field.Key.Value,
                field.Label,
                field.Order,
                (BusinessObjectSolutionFieldType)field.FieldType,
                choice,
                bindingKeys.Select(key => key!).ToArray()));
        }

        return new(
            definition.InstalledComponentKey!,
            definition.Key.Value,
            definition.Name,
            fields);
    }

    private static bool ChoiceOptionsMatch(
        IReadOnlyList<BusinessObjectChoiceOption> options,
        IReadOnlyList<BusinessObjectChoiceOptionSpec> specs) =>
        options.Count == specs.Count && options.Zip(specs).All(pair =>
            StringComparer.Ordinal.Equals(pair.First.Key.Value, pair.Second.OptionKey) &&
            StringComparer.Ordinal.Equals(pair.First.Label, pair.Second.Label) &&
            pair.First.Order == pair.Second.Order);

    private static Result Stamp(
        BusinessObjectDefinition definition,
        string componentKey,
        BusinessObjectDefinitionInstallationReceipt receipt) =>
        definition.AdvanceInstallationReceipt(
            receipt.SolutionVersionId,
            componentKey,
            receipt.ComponentHash,
            receipt.OperationId,
            receipt.StepId,
            receipt.LeaseEpoch);

    private static bool ValidReceipt(BusinessObjectDefinitionInstallationReceipt? receipt) =>
        receipt is not null && receipt.SolutionVersionId != Guid.Empty &&
        receipt.Actor.Id != Guid.Empty && Enum.IsDefined(receipt.Actor.Kind) &&
        receipt.OperationId != Guid.Empty && receipt.StepId != Guid.Empty &&
        receipt.LeaseEpoch > 0 && IsSha256(receipt.ComponentHash);

    private static bool ValidText(string? value) =>
        value is { Length: > 0 and <= 256 } && value == value.Trim() &&
        value.IsNormalized(NormalizationForm.FormC);

    private static bool StrictlySortedUnique(IReadOnlyList<string> values) =>
        values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
        values.Count == values.Distinct(StringComparer.Ordinal).Count();

    private static bool ValidComponentKey(string? value) =>
        value is { Length: > 0 and <= 200 } && value[0] is >= 'a' and <= 'z' &&
        value.Skip(1).All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or ':' or '@' or '-');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit) &&
        StringComparer.Ordinal.Equals(value, value.ToLowerInvariant());

    private static BusinessObjectDefinitionInstallationResult Invalid() =>
        new(false, "businessObjects.definition_component_invalid");

    private static BusinessObjectDefinitionInstallationResult Conflict() =>
        new(false, "businessObjects.definition_install_conflict");
}
