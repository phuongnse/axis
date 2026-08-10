using System.Text.Json;
using Axis.BusinessObjects.Contracts;
using Axis.Identity.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;

namespace Axis.Api.Solutions;

internal sealed class BusinessObjectDefinitionSolutionAdapter(
    IBusinessObjectDefinitionSolutionInstaller installer) : ISolutionComponentAdapter
{
    public const string Type = "business-object.definition.v1";
    public string ComponentType => Type;

    public async Task PreflightAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        CancellationToken cancellationToken = default)
    {
        BusinessObjectDefinitionSolutionComponent parsed = Parse(component);
        BusinessObjectDefinitionInstallationResult result = await installer.ValidateAsync(
            workspaceId,
            parsed,
            cancellationToken);
        if (!result.IsSuccess)
            throw Failure(result.ProblemCode);
    }

    public async Task ApplyAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        SolutionApplyReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        BusinessObjectDefinitionInstallationResult result = await installer.InstallAsync(
            workspaceId,
            Parse(component),
            new BusinessObjectDefinitionInstallationReceipt(
                receipt.SolutionVersionId,
                new SubjectReference(
                    receipt.ActorSubjectKind == SolutionSubjectKind.Service
                        ? SubjectKind.Service
                        : SubjectKind.Human,
                    receipt.ActorSubjectId),
                receipt.ComponentSha256,
                receipt.OperationId,
                receipt.StepId,
                receipt.LeaseEpoch),
            cancellationToken);
        if (!result.IsSuccess)
            throw Failure(result.ProblemCode);
    }

    public async Task<SolutionAdapterReadback> ReadBackAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        SolutionApplyReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        BusinessObjectDefinitionSolutionComponent expected = Parse(component);
        BusinessObjectDefinitionInstallationReadBack? readBack = await installer.ReadBackAsync(
            workspaceId,
            component.Key,
            cancellationToken);
        if (readBack is null)
            return new(false, false);

        bool matches = readBack.WorkspaceId == workspaceId &&
            StringComparer.Ordinal.Equals(readBack.ComponentKey, component.Key) &&
            ContentMatches(readBack.Component, expected) &&
            readBack.SolutionVersionId == receipt.SolutionVersionId &&
            StringComparer.Ordinal.Equals(readBack.ComponentHash, receipt.ComponentSha256) &&
            readBack.OperationId == receipt.OperationId &&
            readBack.StepId == receipt.StepId &&
            readBack.LeaseEpoch == receipt.LeaseEpoch;
        return matches
            ? new(true, false)
            : new(false, true, "businessObjects.definition_readback_mismatch");
    }

    private static BusinessObjectDefinitionSolutionComponent Parse(
        SolutionAdapterPreflight component)
    {
        if (!StringComparer.Ordinal.Equals(component.Type, Type))
            throw Invalid();

        try
        {
            using JsonDocument document = CanonicalSolutionComponentJson.Parse(component.Content);
            JsonElement root = document.RootElement;
            RequireProperties(root, "schemaVersion", "objectKey", "name", "fields");
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw Invalid();

            string objectKey = RequiredString(root, "objectKey");
            if (!StringComparer.Ordinal.Equals(objectKey, component.Key))
                throw Invalid();

            List<BusinessObjectDefinitionSolutionField> fields = [];
            JsonElement fieldsElement = root.GetProperty("fields");
            if (fieldsElement.ValueKind != JsonValueKind.Array || fieldsElement.GetArrayLength() == 0)
                throw Invalid();
            foreach (JsonElement field in fieldsElement.EnumerateArray())
                fields.Add(ParseField(field));

            BusinessObjectDefinitionSolutionComponent parsed = new(
                component.Key,
                objectKey,
                RequiredString(root, "name"),
                fields);
            ValidateDependencies(component.DependsOn, fields);
            return parsed;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static BusinessObjectDefinitionSolutionField ParseField(JsonElement field)
    {
        bool hasChoice = field.TryGetProperty("choiceConfiguration", out JsonElement choice);
        RequireProperties(
            field,
            hasChoice
                ? ["fieldKey", "label", "order", "fieldType", "choiceConfiguration", "bindingKeys"]
                : ["fieldKey", "label", "order", "fieldType", "bindingKeys"]);

        string fieldType = RequiredString(field, "fieldType");
        if (!Enum.TryParse(
                fieldType,
                ignoreCase: false,
                out BusinessObjectSolutionFieldType parsedFieldType) ||
            parsedFieldType.ToString() != fieldType)
        {
            throw Invalid();
        }

        return new(
            RequiredString(field, "fieldKey"),
            RequiredString(field, "label"),
            field.GetProperty("order").GetInt32(),
            parsedFieldType,
            hasChoice ? ParseChoice(choice) : null,
            Strings(field, "bindingKeys"));
    }

    private static BusinessObjectSolutionChoiceConfiguration ParseChoice(JsonElement choice)
    {
        RequireProperties(choice, "selectionMode", "options");
        string selectionMode = RequiredString(choice, "selectionMode");
        if (!Enum.TryParse(
                selectionMode,
                ignoreCase: false,
                out BusinessObjectSolutionChoiceSelectionMode parsedSelectionMode) ||
            parsedSelectionMode.ToString() != selectionMode)
        {
            throw Invalid();
        }

        JsonElement options = choice.GetProperty("options");
        if (options.ValueKind != JsonValueKind.Array || options.GetArrayLength() == 0)
            throw Invalid();
        List<BusinessObjectSolutionChoiceOption> parsedOptions = [];
        foreach (JsonElement option in options.EnumerateArray())
        {
            RequireProperties(option, "optionKey", "label", "order");
            parsedOptions.Add(new(
                RequiredString(option, "optionKey"),
                RequiredString(option, "label"),
                option.GetProperty("order").GetInt32()));
        }
        return new(parsedSelectionMode, parsedOptions);
    }

    private static void ValidateDependencies(
        IReadOnlyList<SolutionComponentReference> dependencies,
        IReadOnlyList<BusinessObjectDefinitionSolutionField> fields)
    {
        string[] expected = fields.SelectMany(field => field.BindingKeys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = dependencies
            .Where(dependency => StringComparer.Ordinal.Equals(dependency.Type, RuleBindingSolutionAdapter.Type))
            .Select(dependency => dependency.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (dependencies.Count != expected.Length ||
            !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Invalid();
        }
    }

    private static IReadOnlyList<string> Strings(JsonElement value, string name)
    {
        JsonElement array = value.GetProperty(name);
        if (array.ValueKind != JsonValueKind.Array)
            throw Invalid();
        List<string> result = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string text)
                throw Invalid();
            result.Add(text);
        }
        return result;
    }

    private static bool ContentMatches(
        BusinessObjectDefinitionSolutionComponent actual,
        BusinessObjectDefinitionSolutionComponent expected) =>
        StringComparer.Ordinal.Equals(actual.ComponentKey, expected.ComponentKey) &&
        StringComparer.Ordinal.Equals(actual.ObjectKey, expected.ObjectKey) &&
        StringComparer.Ordinal.Equals(actual.Name, expected.Name) &&
        actual.Fields.Count == expected.Fields.Count &&
        actual.Fields.Zip(expected.Fields).All(pair => FieldMatches(pair.First, pair.Second));

    private static bool FieldMatches(
        BusinessObjectDefinitionSolutionField actual,
        BusinessObjectDefinitionSolutionField expected) =>
        StringComparer.Ordinal.Equals(actual.FieldKey, expected.FieldKey) &&
        StringComparer.Ordinal.Equals(actual.Label, expected.Label) &&
        actual.Order == expected.Order &&
        actual.FieldType == expected.FieldType &&
        actual.BindingKeys.SequenceEqual(expected.BindingKeys, StringComparer.Ordinal) &&
        ChoiceMatches(actual.ChoiceConfiguration, expected.ChoiceConfiguration);

    private static bool ChoiceMatches(
        BusinessObjectSolutionChoiceConfiguration? actual,
        BusinessObjectSolutionChoiceConfiguration? expected) =>
        actual is null && expected is null ||
        actual is not null && expected is not null &&
        actual.SelectionMode == expected.SelectionMode &&
        actual.Options.Count == expected.Options.Count &&
        actual.Options.Zip(expected.Options).All(pair =>
            StringComparer.Ordinal.Equals(pair.First.OptionKey, pair.Second.OptionKey) &&
            StringComparer.Ordinal.Equals(pair.First.Label, pair.Second.Label) &&
            pair.First.Order == pair.Second.Order);

    private static string RequiredString(JsonElement value, string name) =>
        value.GetProperty(name).ValueKind == JsonValueKind.String &&
        !string.IsNullOrEmpty(value.GetProperty(name).GetString())
            ? value.GetProperty(name).GetString()!
            : throw Invalid();

    private static void RequireProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(property => property.Name)
                .SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Invalid();
        }
    }

    private static SolutionAdapterException Failure(string? problemCode) =>
        new(
            problemCode ?? "businessObjects.definition_install_failed",
            retryable: problemCode is "businessObjects.definition_binding_unavailable"
                or "businessObjects.definition_install_conflict");

    private static SolutionAdapterException Invalid() =>
        new("businessObjects.definition_component_invalid", retryable: false);
}
