using System.Text.Json;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;

namespace Axis.Api.Solutions;

internal sealed class RuleBindingSolutionAdapter(
    IRuleBindingSolutionInstaller installer) : ISolutionComponentAdapter
{
    public const string Type = "rule.binding.v1";
    public string ComponentType => Type;

    public async Task PreflightAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        CancellationToken cancellationToken = default)
    {
        RuleBindingInstallationResult result = await installer.ValidateAsync(
            workspaceId,
            Parse(component),
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
        RuleBindingInstallationResult result = await installer.InstallAsync(
            workspaceId,
            Parse(component),
            new RuleBindingInstallationReceipt(
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
        RuleBindingSolutionComponent expected = Parse(component);
        RuleBindingInstallationReadBack? readBack = await installer.ReadBackAsync(
            workspaceId,
            component.Key,
            cancellationToken);
        if (readBack is null)
            return new(false, false);

        bool matches = readBack.WorkspaceId == workspaceId &&
            readBack.ComponentKey == component.Key &&
            ContentMatches(readBack.Component, expected) &&
            readBack.SolutionVersionId == receipt.SolutionVersionId &&
            readBack.ComponentHash == receipt.ComponentSha256 &&
            readBack.OperationId == receipt.OperationId &&
            readBack.StepId == receipt.StepId &&
            readBack.LeaseEpoch == receipt.LeaseEpoch;
        return matches
            ? new(true, false)
            : new(false, true, "rules.binding_readback_mismatch");
    }

    private static bool ContentMatches(
        RuleBindingSolutionComponent actual,
        RuleBindingSolutionComponent expected) =>
        actual.ComponentKey == expected.ComponentKey &&
        actual.DefinitionKey == expected.DefinitionKey &&
        actual.DefinitionVersion == expected.DefinitionVersion &&
        actual.TargetType == expected.TargetType &&
        actual.TargetId == expected.TargetId &&
        actual.UseCaseOrTrigger == expected.UseCaseOrTrigger &&
        actual.Priority == expected.Priority &&
        actual.Enabled == expected.Enabled &&
        actual.FailureBehavior == expected.FailureBehavior &&
        MappingsMatch(actual.InputMappings, expected.InputMappings);

    private static bool MappingsMatch(
        IReadOnlyDictionary<string, RuleInputMappingDto> actual,
        IReadOnlyDictionary<string, RuleInputMappingDto> expected) =>
        actual.Count == expected.Count && actual.All(pair =>
            expected.TryGetValue(pair.Key, out RuleInputMappingDto? candidate) &&
            pair.Value.Kind == candidate.Kind &&
            pair.Value.ContextKey == candidate.ContextKey &&
            pair.Value.LiteralValues.SequenceEqual(
                candidate.LiteralValues,
                StringComparer.Ordinal));

    private static RuleBindingSolutionComponent Parse(SolutionAdapterPreflight component)
    {
        if (component.Type != Type || component.DependsOn.Count != 0)
            throw Invalid();

        try
        {
            using JsonDocument document = CanonicalSolutionComponentJson.Parse(component.Content);
            JsonElement root = document.RootElement;
            RequireProperties(
                root,
                "schemaVersion",
                "definitionKey",
                "definitionVersion",
                "targetType",
                "targetId",
                "useCaseOrTrigger",
                "inputMappings",
                "priority",
                "enabled",
                "failureBehavior");
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw Invalid();

            Dictionary<string, RuleInputMappingDto> mappings = new(StringComparer.Ordinal);
            JsonElement mappingsElement = root.GetProperty("inputMappings");
            if (mappingsElement.ValueKind != JsonValueKind.Object)
                throw Invalid();
            foreach (JsonProperty mapping in mappingsElement.EnumerateObject())
            {
                RuleInputMappingDto value = ParseMapping(mapping.Value);
                if (!mappings.TryAdd(mapping.Name, value))
                    throw Invalid();
            }

            string failureBehavior = RequiredString(root, "failureBehavior");
            if (!Enum.TryParse(
                    failureBehavior,
                    ignoreCase: false,
                    out RuleBindingFailureBehavior parsedFailureBehavior) ||
                parsedFailureBehavior.ToString() != failureBehavior)
                throw Invalid();

            return new(
                component.Key,
                RequiredString(root, "definitionKey"),
                root.GetProperty("definitionVersion").GetInt32(),
                RequiredString(root, "targetType"),
                RequiredString(root, "targetId"),
                RequiredString(root, "useCaseOrTrigger"),
                mappings,
                root.GetProperty("priority").GetInt32(),
                root.GetProperty("enabled").GetBoolean(),
                parsedFailureBehavior);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static RuleInputMappingDto ParseMapping(JsonElement value)
    {
        string kind = RequiredString(value, "kind");
        return kind switch
        {
            "Context" => ParseContextMapping(value),
            "Literal" => ParseLiteralMapping(value),
            _ => throw Invalid(),
        };
    }

    private static RuleInputMappingDto ParseContextMapping(JsonElement value)
    {
        RequireProperties(value, "kind", "contextKey", "literalValues");
        IReadOnlyList<string> literals = Strings(value, "literalValues");
        if (literals.Count != 0)
            throw Invalid();
        return new(RuleInputMappingKind.Context, RequiredString(value, "contextKey"), literals);
    }

    private static RuleInputMappingDto ParseLiteralMapping(JsonElement value)
    {
        RequireProperties(value, "kind", "literalValues");
        return new(RuleInputMappingKind.Literal, null, Strings(value, "literalValues"));
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
            throw Invalid();
    }

    private static SolutionAdapterException Failure(string? problemCode) =>
        new(
            problemCode ?? "rules.binding_install_failed",
            retryable: problemCode is "rules.binding_install_unavailable" or "rules.binding_install_conflict");

    private static SolutionAdapterException Invalid() =>
        new("rules.binding_component_invalid", retryable: false);
}
