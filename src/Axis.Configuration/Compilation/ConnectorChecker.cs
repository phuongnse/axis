using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the connectors of an application and builds the checked ones. A connector's
/// <c>timeout</c> is a positive duration of at most 2 minutes. Its operation names are unique,
/// and so are the field names of each operation's input and of its output, all ignoring letter
/// case. A path holds no placeholder but <c>{idempotencyKey}</c>. A <c>lookup</c> names another
/// operation of the connector, ignoring letter case, which is a <c>GET</c> whose path holds
/// <c>{idempotencyKey}</c> and whose outputs are those of the operation it serves. The schema
/// already refuses inputs on a <c>GET</c>. Every problem of a connector is reported in one pass.
/// Its bindings are checked at activation, against the tenant's. At compile time,
/// <see cref="CheckBindingKinds"/> reports an <c>oauth2</c> binding whose name the application uses as another kind.
/// <see cref="CheckSlot"/> checks a connector against a module's connector slot, and
/// <see cref="CheckSlotAgainstSlot"/> checks a slot against the slot of a depending module.
/// </summary>
internal static class ConnectorChecker
{
    private const string IdempotencyKeyPlaceholder = "idempotencyKey";

    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Checks <paramref name="connectors"/>, which come in path order.</summary>
    public static void Check(IReadOnlyList<ConnectorResource> connectors, List<Diagnostic> diagnostics)
    {
        foreach (var connector in connectors)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, connector.File, path, connector.Id));

            if (connector.Timeout is { } timeout && !TryParseTimeout(timeout, out _))
            {
                Report(
                    DiagnosticCodes.InvalidConnectorTimeout,
                    $"The timeout '{timeout}' is not allowed. Use a positive ISO 8601 duration of at most PT2M, such as 'PT30S'.",
                    "/timeout");
            }

            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < connector.Operations.Count; index++)
            {
                var operation = connector.Operations[index];
                var path = $"/operations/{index}";
                if (!firstIndexByName.TryAdd(operation.Name, index))
                {
                    Report(
                        DiagnosticCodes.DuplicateConnectorName,
                        $"The operation name '{operation.Name}' is already used by operation '{connector.Operations[firstIndexByName[operation.Name]].Name}' of this connector.",
                        $"{path}/name");
                }

                CheckFieldNames(operation.Input, $"{path}/input", "input", Report);
                CheckFieldNames(operation.Output, $"{path}/output", "output", Report);
                if (FindPathProblem(operation.Path) is { } problem)
                {
                    Report(DiagnosticCodes.InvalidOperationPath, $"The path '{operation.Path}' {problem}", $"{path}/path");
                }
            }

            for (var index = 0; index < connector.Operations.Count; index++)
            {
                var operation = connector.Operations[index];
                if (operation.Lookup is { } lookup && FindLookupProblem(connector, operation, lookup) is { } problem)
                {
                    Report(DiagnosticCodes.InvalidOperationLookup, problem, $"/operations/{index}/lookup");
                }
            }
        }
    }

    /// <summary>
    /// Reports each <c>oauth2</c> binding of <paramref name="connectors"/> whose name the application
    /// also uses as another kind, ignoring letter case, at <c>/auth/&lt;property&gt;/binding</c>. The
    /// kind of a name is read from where it is used, as for an identity provider's bindings.
    /// </summary>
    public static void CheckBindingKinds(
        IReadOnlyList<ConnectorResource> connectors,
        ApplicationManifest? manifest,
        IReadOnlyList<IdentityProviderResource> providers,
        List<Diagnostic> diagnostics)
    {
        if (!connectors.Any(connector => connector.Auth is { Type: "oauth2" }))
        {
            return;
        }

        var uses = BindingUses.Collect(providers, manifest, connectors);
        foreach (var connector in connectors.Where(connector => connector.Auth is { Type: "oauth2" }))
        {
            foreach (var (property, kind, binding) in BindingUses.BindingsOf(connector).Where(use => use.Property.StartsWith("auth/", StringComparison.Ordinal)))
            {
                if (uses.TryGetValue(binding.Binding, out var candidates)
                    && candidates.FirstOrDefault(candidate => candidate.Kind != kind) is { } other)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.ConnectorBindingKind,
                        $"The binding '{binding.Binding}' is a {kind} binding here, but '{other.File}' uses it as a {other.Kind} binding at '{other.Path}'.",
                        connector.File,
                        $"/{property}/binding",
                        connector.Id));
                }
            }
        }
    }

    /// <summary>
    /// Checks that <paramref name="connector"/> fits <paramref name="slot"/>, and reports every
    /// problem in one pass through <paramref name="report"/>, with a code and a message. Each slot
    /// operation must be an operation of the connector, ignoring letter case. Each slot input must be
    /// an input of that operation with the same type, every required input of the operation must be a
    /// slot input, and each slot output must be an output of the operation with the same type. Field
    /// names are matched ignoring letter case, and a field's <c>required</c> is not compared.
    /// </summary>
    public static void CheckSlot(ModuleConnectorSlot slot, ConnectorResource connector, Action<string, string> report)
    {
        CheckTypes(
            $"slot '{slot.Name}'",
            slot,
            $"connector '{connector.Name}'",
            name => Find(connector, name) is { } operation ? (operation.Name, operation.Input, operation.Output) : null,
            report);
        CheckRequiredInputs(slot, connector, report);
    }

    /// <summary>
    /// Checks that <paramref name="target"/>, the slot that <paramref name="slot"/> is mapped to,
    /// fits <paramref name="slot"/>: the operations and the input and output types, as
    /// <see cref="CheckSlot"/> does. A required input is not checked, because a slot's own
    /// <c>required</c> is not compared. <see cref="CheckRequiredInputs"/> checks it against the
    /// connector that the chain reaches.
    /// </summary>
    public static void CheckSlotAgainstSlot(
        ModuleConnectorSlot slot, string slotModule, ModuleConnectorSlot target, string targetModule, Action<string, string> report) =>
        CheckTypes(
            $"slot '{slot.Name}' of module '{slotModule}'",
            slot,
            $"slot '{target.Name}' of module '{targetModule}'",
            name => target.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) is { } operation
                ? (operation.Name, operation.Input, operation.Output)
                : null,
            report);

    /// <summary>
    /// Checks that every required input of each operation of <paramref name="connector"/> that
    /// <paramref name="slot"/> uses is an input of the slot operation, ignoring letter case.
    /// </summary>
    public static void CheckRequiredInputs(ModuleConnectorSlot slot, ConnectorResource connector, Action<string, string> report)
    {
        foreach (var slotOperation in slot.Operations)
        {
            if (Find(connector, slotOperation.Name) is not { } operation)
            {
                continue;
            }

            foreach (var field in operation.Input.Where(input => input.Required))
            {
                if (FindField(slotOperation.Input, field.Name) is null)
                {
                    report(
                        DiagnosticCodes.MissingSlotRequiredInput,
                        $"Operation '{operation.Name}' of connector '{connector.Name}' requires the input '{field.Name}', but slot '{slot.Name}' has no input '{field.Name}' for operation '{slotOperation.Name}'.");
                }
            }
        }
    }

    /// <summary>
    /// Checks the operations of <paramref name="slot"/> and the types of their inputs and outputs
    /// against a target, which <paramref name="find"/> looks up by operation name. <paramref name="slotText"/>
    /// and <paramref name="owner"/> say in messages which slot and which target are meant.
    /// </summary>
    private static void CheckTypes(
        string slotText,
        ModuleConnectorSlot slot,
        string owner,
        Func<string, (string Name, IReadOnlyList<ConnectorFieldDefinition> Input, IReadOnlyList<ConnectorFieldDefinition> Output)?> find,
        Action<string, string> report)
    {
        var capitalized = char.ToUpperInvariant(slotText[0]) + slotText[1..];
        foreach (var slotOperation in slot.Operations)
        {
            if (find(slotOperation.Name) is not { } operation)
            {
                report(
                    DiagnosticCodes.MissingSlotOperation,
                    $"The {owner} has no operation '{slotOperation.Name}', which {slotText} needs.");
                continue;
            }

            foreach (var field in slotOperation.Input)
            {
                var match = FindField(operation.Input, field.Name);
                if (match is null || match.Type != field.Type)
                {
                    report(
                        DiagnosticCodes.MismatchedSlotInput,
                        match is null
                            ? $"{capitalized} sends the input '{field.Name}' of type '{field.Type}' to operation '{operation.Name}', but operation '{operation.Name}' of {owner} has no input '{field.Name}'."
                            : $"{capitalized} sends the input '{field.Name}' of type '{field.Type}' to operation '{operation.Name}', but operation '{operation.Name}' of {owner} has the input '{match.Name}' of type '{match.Type}'.");
                }
            }

            foreach (var field in slotOperation.Output)
            {
                var match = FindField(operation.Output, field.Name);
                if (match is null || match.Type != field.Type)
                {
                    report(
                        DiagnosticCodes.MismatchedSlotOutput,
                        match is null
                            ? $"{capitalized} reads the output '{field.Name}' of type '{field.Type}' from operation '{operation.Name}', but operation '{operation.Name}' of {owner} has no output '{field.Name}'."
                            : $"{capitalized} reads the output '{field.Name}' of type '{field.Type}' from operation '{operation.Name}', but operation '{operation.Name}' of {owner} has the output '{match.Name}' of type '{match.Type}'.");
                }
            }
        }
    }

    /// <summary>Builds a checked connector. A lookup is resolved to the declared name of its operation.</summary>
    public static ConnectorModel Build(ConnectorResource connector)
    {
        if (!TryParseTimeout(connector.Timeout, out var timeout))
        {
            throw new ArgumentException("Only a checked connector can be built.", nameof(connector));
        }

        return new ConnectorModel
        {
            Id = connector.Id,
            Name = connector.Name,
            File = connector.File,
            BaseUrlBinding = connector.BaseUrl.Binding,
            Auth = connector.Auth switch
            {
                { Type: "bearer", Token: { } token } => new ConnectorAuthModel("bearer", null, token.Binding, "/auth/token/binding"),
                { Type: "header", Name: { } name, Value: { } value } => new ConnectorAuthModel("header", name, value.Binding, "/auth/value/binding"),
                { Type: "oauth2", TokenUrl: { } tokenUrl, ClientId: { } clientId, ClientSecret: { } clientSecret } =>
                    new ConnectorAuthModel("oauth2", null, clientSecret.Binding, "/auth/clientSecret/binding")
                    {
                        OAuth2 = new ConnectorOAuth2Model(tokenUrl.Binding, clientId.Binding, connector.Auth.Scope, connector.Auth.Audience),
                    },
                _ => null,
            },
            Timeout = timeout,
            Idempotency = connector.Idempotency,
            Operations = connector.Operations
                .Select(operation => new ConnectorOperationModel(
                    operation.Name,
                    operation.Method,
                    operation.Path,
                    operation.Input.Select(BuildField).ToList(),
                    operation.Output.Select(BuildField).ToList(),
                    operation.Lookup is { } lookup ? Find(connector, lookup)!.Name : null))
                .ToList(),
        };
    }

    private static ConnectorFieldModel BuildField(ConnectorFieldDefinition field) =>
        new(field.Name, FieldTypes.Parse(field.Type), field.Required);

    /// <summary>Parses a timeout, which is 30 seconds when not written.</summary>
    private static bool TryParseTimeout(string? text, out TimeSpan timeout)
    {
        if (text is null)
        {
            timeout = _defaultTimeout;
            return true;
        }

        return IsoDuration.TryParse(text, out timeout) && timeout <= _maxTimeout;
    }

    private static void CheckFieldNames(
        IReadOnlyList<ConnectorFieldDefinition> fields, string path, string list, Action<string, string, string> report)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            if (!names.Add(fields[index].Name))
            {
                report(
                    DiagnosticCodes.DuplicateConnectorName,
                    $"The {list} field name '{fields[index].Name}' is already used by an earlier {list} field of this operation.",
                    $"{path}/{index}/name");
            }
        }
    }

    /// <summary>Describes the first problem of a path template, or gives null when it has none.</summary>
    private static string? FindPathProblem(string path)
    {
        var position = 0;
        while (position < path.Length)
        {
            var open = path.IndexOfAny(['{', '}'], position);
            if (open < 0)
            {
                return null;
            }

            var close = open + 1 < path.Length ? path.IndexOfAny(['{', '}'], open + 1) : -1;
            if (path[open] == '}' || close < 0 || path[close] == '{')
            {
                return "has an unbalanced '{' or '}'.";
            }

            var placeholder = path[(open + 1)..close];
            if (placeholder != IdempotencyKeyPlaceholder)
            {
                return $"has the placeholder '{{{placeholder}}}'. '{{{IdempotencyKeyPlaceholder}}}' is the only placeholder, and inputs are sent in the JSON body.";
            }

            position = close + 1;
        }

        return null;
    }

    /// <summary>Describes why a lookup cannot serve its operation, or gives null when it can.</summary>
    private static string? FindLookupProblem(ConnectorResource connector, ConnectorOperationDefinition operation, string lookup)
    {
        if (Find(connector, lookup) is not { } target)
        {
            return $"The lookup '{lookup}' was not found. No operation of this connector has that name.";
        }

        if (ReferenceEquals(target, operation))
        {
            return $"The lookup '{lookup}' names its own operation. Name another operation of this connector.";
        }

        if (target.Method != "GET")
        {
            return $"The lookup '{lookup}' is a {target.Method} operation. A lookup is a GET.";
        }

        if (!target.Path.Contains($"{{{IdempotencyKeyPlaceholder}}}", StringComparison.Ordinal))
        {
            return $"The lookup '{lookup}' has no '{{{IdempotencyKeyPlaceholder}}}' in its path, so it cannot find an earlier call.";
        }

        if (!SameFields(operation.Output, target.Output))
        {
            return $"The lookup '{lookup}' has other outputs than operation '{operation.Name}'. Give it the same output names, types and 'required'.";
        }

        return null;
    }

    /// <summary>Finds the first operation with <paramref name="name"/>, ignoring letter case.</summary>
    private static ConnectorOperationDefinition? Find(ConnectorResource connector, string name) =>
        connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the first field with <paramref name="name"/>, ignoring letter case.</summary>
    private static ConnectorFieldDefinition? FindField(IReadOnlyList<ConnectorFieldDefinition> fields, string name) =>
        fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether two field lists have the same names, ignoring letter case and order, with the same type and <c>required</c>.</summary>
    private static bool SameFields(IReadOnlyList<ConnectorFieldDefinition> left, IReadOnlyList<ConnectorFieldDefinition> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        // A list that repeats a name is already reported, and never matches another.
        var rightByName = new Dictionary<string, ConnectorFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in right)
        {
            rightByName.TryAdd(field.Name, field);
        }

        return rightByName.Count == right.Count
            && left.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == left.Count
            && left.All(field => rightByName.TryGetValue(field.Name, out var match) && match.Type == field.Type && match.Required == field.Required);
    }
}
