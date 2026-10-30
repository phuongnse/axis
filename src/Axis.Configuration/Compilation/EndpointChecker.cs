using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the endpoints of an application and builds the checked ones. An endpoint names a loaded
/// root entity and, when it names one, a loaded process whose subject entity is that entity. Either
/// name may be <c>&lt;module id&gt;/&lt;name&gt;</c> of a module the application declares, and a
/// name under a module it does not declare is <c>AXC0207</c>. Its input fields have
/// unique names, ignoring letter case, and the type properties of an entity field. A
/// child-collection input targets a child entity. Each <c>values</c> key names a field of the
/// entity, ignoring letter case and at most once, that is not computed and has no
/// <c>sequence</c> or <c>creator</c>. A child-collection field takes, by name, a child-collection
/// input with the same target. Any other field takes an expression that sees only the input fields
/// and gives the field's type. Each <c>output</c> entry names a field of the entity. A
/// <c>wait</c> is an ISO 8601 duration, more than zero and at most 30 seconds. Every problem of an
/// endpoint is reported in one pass. The mapping and the output are not checked without the
/// entity, and the mapping is not matched against inputs that have a diagnostic of their own. An
/// optional <c>event</c> is checked with <see cref="RaiseEventChecks"/> like a raise event step,
/// with expressions that see only the input fields. When a webhook names the event, no expression
/// reads an input that <c>values</c> maps to a sensitive field. The event is checked without the
/// entity, and its expressions are skipped while an input has a diagnostic of its own.
/// </summary>
internal static class EndpointChecker
{
    /// <summary>The longest a call can wait for its instance to complete.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checks <paramref name="endpoints"/>, which come in path order. <paramref name="ownerOf"/>
    /// names the owner of a child entity, or gives null for a root entity.
    /// <paramref name="checkInputField"/> checks an input as an entity field, at its path.
    /// </summary>
    public static void Check(
        IReadOnlyList<EndpointResource> endpoints,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        IReadOnlySet<string> webhookEvents,
        Action<FieldDefinition, string, Action<string, string, string>> checkInputField,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        foreach (var endpoint in endpoints)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, endpoint.File, path, endpoint.Id));

            var entity = moduleReferences.Find(endpoint.Entity, findEntity);
            if (entity is null)
            {
                // An entity file that was not loaded because of its own errors is not reported again.
                if (!unloadedEntityNames.Contains(endpoint.Entity)
                    && !moduleReferences.ReportUndeclared(endpoint.Entity, "/entity", Report))
                {
                    Report(
                        DiagnosticCodes.UnknownEndpointEntity,
                        $"The entity '{endpoint.Entity}' was not found. No loaded entity has that name.",
                        "/entity");
                }
            }
            else
            {
                references.Add(endpoint, "/entity", ReferenceRelations.EndpointEntity, ResourceNode.Of(entity));
            }

            if (entity is not null && ownerOf(entity.Name) is { } owner)
            {
                Report(
                    DiagnosticCodes.EndpointOverChildEntity,
                    $"The entity '{entity.Name}' is a child entity owned by '{owner}'. An endpoint creates a record of a root entity.",
                    "/entity");
            }

            if (endpoint.Process is { } processName)
            {
                var process = moduleReferences.Find(processName, findProcess);
                if (process is null)
                {
                    if (!unloadedProcessNames.Contains(processName)
                        && !moduleReferences.ReportUndeclared(processName, "/process", Report))
                    {
                        Report(
                            DiagnosticCodes.UnknownEndpointProcess,
                            $"The process '{processName}' was not found. No loaded process has that name.",
                            "/process");
                    }
                }
                else
                {
                    references.Add(endpoint, "/process", ReferenceRelations.EndpointProcess, ResourceNode.Of(process));
                }

                if (process is { Entity: null })
                {
                    // The call starts the process on the record it creates, so the process needs a subject.
                    Report(
                        DiagnosticCodes.EndpointProcessOverOtherEntity,
                        $"The process '{process.Name}' has no subject, but the endpoint starts it on the record it creates.",
                        "/process");
                }
                else if (process is not null && entity is not null && process.Entity is { } subjectName && findEntity(subjectName) is { } subject && !ReferenceEquals(subject, entity))
                {
                    // The call starts the process on the record it creates, so that record is its subject.
                    Report(
                        DiagnosticCodes.EndpointProcessOverOtherEntity,
                        $"The process '{process.Name}' is over the entity '{subject.Name}', but the endpoint creates records of '{entity.Name}'.",
                        "/process");
                }
            }

            var countBeforeInput = diagnostics.Count;
            CheckInput(endpoint, findEntity, ownerOf, checkInputField, references, Report);
            var inputValid = diagnostics.Count == countBeforeInput;

            if (endpoint.Wait is { } wait
                && !(IsoDuration.TryParse(wait, out var duration) && duration > TimeSpan.Zero && duration <= MaxWait))
            {
                Report(
                    DiagnosticCodes.InvalidEndpointWait,
                    $"The wait '{wait}' must be an ISO 8601 duration such as 'PT10S', more than zero and at most 'PT30S'.",
                    "/wait");
            }

            if (endpoint.Event is { } raised)
            {
                // The event is checked without the entity, and its expressions only while the input is valid.
                RaiseEventChecks.Check(
                    endpoint,
                    raised.Name,
                    raised.Fields,
                    "/event/name",
                    "/event/fields",
                    ReferenceRelations.EndpointEvent,
                    "endpoint",
                    "An endpoint can raise only an event about its entity '{0}'.",
                    entity,
                    inputValid ? ExpressionScopes.ForEndpointInput(endpoint.Input) : null,
                    findEntity,
                    name => moduleReferences.Find(name, findEvent),
                    name => unloadedEventNames.Contains(name) || moduleReferences.ReportUndeclared(name, "/event/name", Report),
                    webhookEvents,
                    (node, _) => SensitiveInputRead(endpoint, entity, node),
                    references,
                    Report);
            }

            if (entity is null)
            {
                // The mapping and the output cannot be checked without the entity.
                continue;
            }

            CheckValues(endpoint, entity, findEntity, inputValid, references, Report);

            var output = endpoint.Output ?? [];
            for (var index = 0; index < output.Count; index++)
            {
                if (ApplicationCompiler.FindField(entity, output[index]) is null)
                {
                    Report(
                        DiagnosticCodes.UnknownEndpointOutputField,
                        $"The field '{output[index]}' was not found. The entity '{entity.Name}' has no field with that name.",
                        $"/output/{index}");
                }
            }
        }
    }

    /// <summary>
    /// Builds a checked endpoint. Its entity, process and input targets are resolved, its mapping
    /// expressions are compiled over the input fields, and its mapped and output fields are named
    /// as the entity declares them.
    /// </summary>
    public static EndpointModel Build(
        EndpointResource endpoint,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        Func<string, EventResource?> findEvent)
    {
        var entity = findEntity(endpoint.Entity)
            ?? throw new ArgumentException("Only a checked endpoint can be built.", nameof(endpoint));
        var process = endpoint.Process is { } processName ? findProcess(processName)! : null;
        var scope = ExpressionScopes.ForEndpointInput(endpoint.Input);

        var values = new List<EndpointValueModel>();
        foreach (var field in entity.Fields)
        {
            var mapping = endpoint.Values.FirstOrDefault(value => string.Equals(value.Key, field.Name, StringComparison.OrdinalIgnoreCase));
            if (mapping.Key is null)
            {
                continue;
            }

            values.Add(FieldTypes.Parse(field.Type) == FieldType.ChildCollection
                ? new EndpointValueModel(field.Name, null, FindCollectionInput(endpoint, mapping.Value)!.Name)
                : new EndpointValueModel(field.Name, ExpressionModel.Compile(mapping.Value, scope, ExpressionScopes.TypeOf(field)!), null));
        }

        TimeSpan? wait = null;
        if (endpoint.Wait is { } waitText && IsoDuration.TryParse(waitText, out var duration))
        {
            wait = duration;
        }

        EndpointEventModel? raisedEvent = null;
        if (endpoint.Event is { } raised)
        {
            // A checked event is loaded and gets each of its payload fields once.
            var @event = findEvent(raised.Name)!;
            var fields = new Dictionary<string, string>(raised.Fields, StringComparer.OrdinalIgnoreCase);
            raisedEvent = new EndpointEventModel(
                @event.Name,
                [
                    .. @event.Fields.Select(field => new EventPayloadModel(
                        field.Name,
                        ExpressionModel.Compile(fields[field.Name], scope, ExpressionScopes.TypeOf(field.Type)))),
                ]);
        }

        return new EndpointModel
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            File = endpoint.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Process = process is null ? null : new ProcessReference(process.Id, process.Name),
            Event = raisedEvent,
            Input = endpoint.Input
                .Select(input => new EndpointInputModel(
                    input.Name,
                    FieldTypes.Parse(input.Type),
                    input.Required ?? false,
                    input.MaxLength,
                    input.Precision,
                    input.Scale,
                    input.Values,
                    input.Target is not null && findEntity(input.Target) is { } target ? new EntityReference(target.Id, target.Name) : null))
                .ToList(),
            Values = values,
            Output = endpoint.Output?.Select(name => ApplicationCompiler.FindField(entity, name)!.Name).ToList(),
            Wait = wait,
        };
    }

    /// <summary>
    /// Describes the first input that <paramref name="node"/> reads and that a <c>values</c>
    /// expression of a sensitive field reads too, or gives null. It counts every input name such an
    /// expression mentions, not only an expression that is the name alone, so that
    /// <c>upper(token)</c> still counts as copying <c>token</c>.
    /// </summary>
    private static string? SensitiveInputRead(EndpointResource endpoint, EntityResource? entity, ExpressionNode node)
    {
        if (entity is null)
        {
            return null;
        }

        // The sensitive field that reads each input, by the input's name.
        var sensitiveByInput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in endpoint.Values)
        {
            if (ApplicationCompiler.FindField(entity, key) is not { Sensitive: true } field
                || FieldTypes.Parse(field.Type) == FieldType.ChildCollection)
            {
                continue;
            }

            var parsed = ExpressionParser.Parse(value);
            if (!parsed.Succeeded)
            {
                continue;
            }

            foreach (var name in Names(parsed.Expression!))
            {
                sensitiveByInput.TryAdd(name, field.Name);
            }
        }

        foreach (var name in Names(node))
        {
            if (sensitiveByInput.TryGetValue(name, out var fieldName))
            {
                return $"the input '{name}', which 'values' maps to the sensitive field '{entity.Name}.{fieldName}'";
            }
        }

        return null;
    }

    /// <summary>The names that <paramref name="node"/> reads, in order of appearance.</summary>
    private static IEnumerable<string> Names(ExpressionNode node)
    {
        switch (node)
        {
            case NameNode name:
                yield return name.Name;
                break;
            case MemberNode member:
                foreach (var inner in Names(member.Target))
                {
                    yield return inner;
                }

                break;
            case UnaryNode unary:
                foreach (var inner in Names(unary.Operand))
                {
                    yield return inner;
                }

                break;
            case BinaryNode binary:
                foreach (var inner in Names(binary.Left).Concat(Names(binary.Right)))
                {
                    yield return inner;
                }

                break;
            case IsNullNode isNull:
                foreach (var inner in Names(isNull.Operand))
                {
                    yield return inner;
                }

                break;
            case InNode inList:
                foreach (var inner in Names(inList.Operand).Concat(inList.Items.SelectMany(Names)))
                {
                    yield return inner;
                }

                break;
            case CallNode call:
                foreach (var inner in call.Arguments.SelectMany(Names))
                {
                    yield return inner;
                }

                break;
        }
    }

    /// <summary>
    /// Checks each input field as an entity field, then that its name is new, ignoring letter case,
    /// and that a child-collection input targets a child entity.
    /// </summary>
    private static void CheckInput(
        EndpointResource endpoint,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        Action<FieldDefinition, string, Action<string, string, string>> checkInputField,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < endpoint.Input.Count; index++)
        {
            var input = endpoint.Input[index];
            var path = $"/input/{index}";
            if (!firstIndexByName.TryAdd(input.Name, index))
            {
                report(
                    DiagnosticCodes.DuplicateEndpointInputName,
                    $"The input name '{input.Name}' is already used by input '{endpoint.Input[firstIndexByName[input.Name]].Name}' of this endpoint.",
                    $"{path}/name");
            }

            var definition = new FieldDefinition
            {
                Name = input.Name,
                Type = input.Type,
                Required = input.Required,
                MaxLength = input.MaxLength,
                Precision = input.Precision,
                Scale = input.Scale,
                Values = input.Values,
                Target = input.Target,
            };
            checkInputField(definition, path, report);
            ApplicationCompiler.AddFieldTarget(endpoint, definition, path, findEntity, references);

            // An unknown target is reported by the field check.
            if (FieldTypes.Parse(input.Type) == FieldType.ChildCollection
                && input.Target is not null
                && findEntity(input.Target) is { } target
                && ownerOf(target.Name) is null)
            {
                report(
                    DiagnosticCodes.InvalidEndpointInputTarget,
                    $"The target entity '{target.Name}' is not a child entity. A child-collection input takes the rows of a child entity.",
                    $"{path}/target");
            }
        }
    }

    /// <summary>
    /// Checks each <c>values</c> key and its value. The value is matched against the input only
    /// when <paramref name="inputValid"/> is true, so a broken input is not reported again here.
    /// </summary>
    private static void CheckValues(
        EndpointResource endpoint,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        bool inputValid,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var scope = ExpressionScopes.ForEndpointInput(endpoint.Input);
        var mappedKeyByField = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in endpoint.Values)
        {
            var path = $"/values/{key}";
            var field = ApplicationCompiler.FindField(entity, key);
            if (field is null)
            {
                report(
                    DiagnosticCodes.UnknownEndpointValueField,
                    $"The field '{key}' was not found. The entity '{entity.Name}' has no field with that name.",
                    path);
                continue;
            }

            if (!mappedKeyByField.TryAdd(field.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownEndpointValueField,
                    $"The field '{field.Name}' is already mapped by the key '{mappedKeyByField[field.Name]}'. Keys are compared ignoring letter case.",
                    path);
                continue;
            }

            var readOnly = field.Expression is not null ? "is computed"
                : field.Sequence is not null ? "takes its value from a sequence"
                : field.Creator == true ? "is the record's creator"
                : null;
            if (readOnly is not null)
            {
                report(
                    DiagnosticCodes.ReadOnlyEndpointValueField,
                    $"The field '{field.Name}' {readOnly}, so the server sets it. An endpoint maps only fields it can write.",
                    path);
                continue;
            }

            // A set of records has no storage yet, so there is nothing for a mapping to write.
            if (FieldTypes.Parse(field.Type) == FieldType.References)
            {
                report(
                    DiagnosticCodes.ReadOnlyEndpointValueField,
                    $"The field '{field.Name}' is a references field. An endpoint cannot map a set of records yet.",
                    path);
                continue;
            }

            if (!inputValid)
            {
                continue;
            }

            if (FieldTypes.Parse(field.Type) == FieldType.ChildCollection)
            {
                var input = FindCollectionInput(endpoint, value);
                if (input is null || !SameEntity(input.Target, field.Target, findEntity))
                {
                    report(
                        DiagnosticCodes.InvalidEndpointCollectionValue,
                        $"The child collection '{field.Name}' takes the name of a child-collection input whose target is '{field.Target}'. '{value}' is not one.",
                        path);
                }

                continue;
            }

            var parsed = ExpressionParser.Parse(value);
            var check = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression!, scope, ExpressionScopes.TypeOf(field)!)
                : null;
            var problem = parsed.Succeeded ? check!.Diagnostic : parsed.Diagnostic;
            references.RecordExpression(endpoint, path, check?.Type, problem);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, path);
            }
        }
    }

    /// <summary>Finds a child-collection input by name, ignoring letter case.</summary>
    private static EndpointInputDefinition? FindCollectionInput(EndpointResource endpoint, string name) =>
        endpoint.Input.FirstOrDefault(input =>
            FieldTypes.Parse(input.Type) == FieldType.ChildCollection
            && string.Equals(input.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether two target names find the same loaded entity, or are equal ignoring letter case.</summary>
    private static bool SameEntity(string? left, string? right, Func<string, EntityResource?> findEntity) =>
        left is not null
        && right is not null
        && (findEntity(left) is { } leftEntity && findEntity(right) is { } rightEntity
            ? ReferenceEquals(leftEntity, rightEntity)
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
}
