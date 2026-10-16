using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the scenarios of an application. Every role of a user is a declared role, every <c>as</c>
/// and user assignee is a user of the scenario, and an assignee role is a declared role. A
/// <c>createRecord</c> step names a loaded root entity. A record alias is saved by one step and
/// used only by later steps. The record's entity is the one its <c>createRecord</c> step names, and
/// it resolves the rest of a step: a process of <c>startProcess</c> or <c>expectInstance</c> is a
/// process over that entity, a task step of <c>completeTask</c> or <c>expectTask</c> is a task step
/// of such a process, and an outcome is an outcome of it. The <c>values</c> of a step name fields of
/// the entity and fit the field's type, item by item for a child collection. Null fits any type, and
/// computed and read-only fields are not refused, because a scenario may test that the record API
/// refuses a value. A connector stub names a loaded connector and one of its operations, a package
/// stub names a pinned package operation and each output names an output field of the operation.
/// Every problem of a scenario is reported in one pass. Names are matched ignoring letter case,
/// except roles, users and aliases, which are matched exactly. A name whose file was not loaded
/// because of its own errors is not reported again, and a step that depends on an unknown alias or
/// entity is not checked further.
/// </summary>
internal static class ScenarioChecker
{
    /// <summary>Checks <paramref name="scenarios"/>, which come in path order.</summary>
    public static void Check(
        IReadOnlyList<ScenarioResource> scenarios,
        IReadOnlySet<string> roleNames,
        IReadOnlySet<string> unloadedRoleNames,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> childOwnerOf,
        IReadOnlySet<string> unloadedEntityNames,
        IReadOnlyList<ProcessResource> processes,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        List<Diagnostic> diagnostics)
    {
        // Duplicate process names are reported by the loader, so names resolve to the first process.
        var processesByName = new Dictionary<string, ProcessResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in processes)
        {
            processesByName.TryAdd(process.Name, process);
        }

        foreach (var scenario in scenarios)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, scenario.File, path, scenario.Id));

            bool KnownRole(string role) => roleNames.Contains(role) || unloadedRoleNames.Contains(role);

            foreach (var (user, roles) in scenario.Users)
            {
                for (var index = 0; index < roles.Count; index++)
                {
                    if (!KnownRole(roles[index]))
                    {
                        Report(
                            DiagnosticCodes.UnknownScenarioRole,
                            $"The role '{roles[index]}' was not found. No loaded role has that name.",
                            $"/users/{Escape(user)}/{index}");
                    }
                }
            }

            for (var index = 0; index < scenario.Stubs.Count; index++)
            {
                CheckStub(
                    scenario.Stubs[index],
                    $"/stubs/{index}",
                    findConnector,
                    unloadedConnectorNames,
                    findPackage,
                    declaredPackageIds,
                    Report);
            }

            var aliases = new Dictionary<string, EntityResource?>(StringComparer.Ordinal);
            for (var index = 0; index < scenario.Steps.Count; index++)
            {
                var step = scenario.Steps[index];
                var path = $"/steps/{index}";

                if (step.As is { } asUser && !scenario.Users.ContainsKey(asUser))
                {
                    Report(
                        DiagnosticCodes.UnknownScenarioUser,
                        $"The user '{asUser}' was not found. The scenario has no user with that id.",
                        $"{path}/as");
                }

                if (step.Assignee?.User is { } assigneeUser && !scenario.Users.ContainsKey(assigneeUser))
                {
                    Report(
                        DiagnosticCodes.UnknownScenarioUser,
                        $"The user '{assigneeUser}' was not found. The scenario has no user with that id.",
                        $"{path}/assignee/user");
                }

                if (step.Assignee?.Role is { } assigneeRole && !KnownRole(assigneeRole))
                {
                    Report(
                        DiagnosticCodes.UnknownScenarioRole,
                        $"The role '{assigneeRole}' was not found. No loaded role has that name.",
                        $"{path}/assignee/role");
                }

                // The record's entity, or null when it is not known. A null entity stops the checks that need it.
                EntityResource? entity = null;
                if (step.Type == ScenarioStepDefinition.CreateRecord)
                {
                    entity = ResolveEntity(step.Entity!, $"{path}/entity", findEntity, childOwnerOf, unloadedEntityNames, Report);
                    if (step.Save is { } save && !aliases.TryAdd(save, entity))
                    {
                        Report(
                            DiagnosticCodes.DuplicateScenarioRecord,
                            $"The alias '{save}' is already saved by an earlier step. Each alias is saved once.",
                            $"{path}/save");
                    }
                }
                else if (step.Record is { } record && !aliases.TryGetValue(record, out entity))
                {
                    Report(
                        DiagnosticCodes.UnsavedScenarioRecord,
                        $"The record '{record}' is not saved by an earlier step. Save it with 'save' on a 'createRecord' step before this one.",
                        $"{path}/record");
                }

                CheckProcessReferences(step, path, entity, findEntity, processes, processesByName, unloadedProcessNames, Report);

                if (step.Values is { } values && entity is not null)
                {
                    CheckValues(entity, values, $"{path}/values", findEntity, Report);
                }
            }
        }
    }

    /// <summary>
    /// Resolves the entity of a <c>createRecord</c> step. An entity whose file was not loaded is not
    /// reported again. A child entity is reported, because only its owner's records hold it.
    /// </summary>
    private static EntityResource? ResolveEntity(
        string name,
        string path,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> childOwnerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Action<string, string, string> report)
    {
        if (findEntity(name) is not { } entity)
        {
            if (!unloadedEntityNames.Contains(name))
            {
                report(
                    DiagnosticCodes.UnknownScenarioEntity,
                    $"The entity '{name}' was not found. No loaded entity has that name.",
                    path);
            }

            return null;
        }

        if (childOwnerOf(entity.Name) is { } owner)
        {
            report(
                DiagnosticCodes.UnknownScenarioEntity,
                $"The entity '{entity.Name}' is a child entity owned by '{owner}'. A step creates a record of a root entity.",
                path);
            return null;
        }

        return entity;
    }

    /// <summary>
    /// Checks the process, step and outcome of a step against the record's entity. A step the record
    /// entity does not resolve, such as one over an unknown alias, is checked as far as it can be.
    /// </summary>
    private static void CheckProcessReferences(
        ScenarioStepDefinition step,
        string path,
        EntityResource? entity,
        Func<string, EntityResource?> findEntity,
        IReadOnlyList<ProcessResource> processes,
        Dictionary<string, ProcessResource> processesByName,
        IReadOnlySet<string> unloadedProcessNames,
        Action<string, string, string> report)
    {
        if (step.Type is ScenarioStepDefinition.StartProcess or ScenarioStepDefinition.ExpectInstance)
        {
            var name = step.Process!;
            if (!processesByName.TryGetValue(name, out var process))
            {
                if (!unloadedProcessNames.Contains(name))
                {
                    report(
                        DiagnosticCodes.UnknownScenarioProcess,
                        $"The process '{name}' was not found. No loaded process has that name.",
                        $"{path}/process");
                }

                return;
            }

            // A process over an unknown entity has a diagnostic of its own.
            if (entity is not null && findEntity(process.Entity) is { } processEntity && !ReferenceEquals(processEntity, entity))
            {
                report(
                    DiagnosticCodes.UnknownScenarioProcess,
                    $"The process '{process.Name}' is over the entity '{processEntity.Name}', but the record is of '{entity.Name}'. A process runs for a record of its own entity.",
                    $"{path}/process");
                return;
            }

            if (step.Type == ScenarioStepDefinition.ExpectInstance
                && step.Step is { } stepName
                && !process.Steps.Any(candidate => Same(candidate.Name, stepName)))
            {
                report(
                    DiagnosticCodes.UnknownScenarioStep,
                    $"The step '{stepName}' was not found. The process '{process.Name}' has no step with that name.",
                    $"{path}/step");
            }

            return;
        }

        if (step.Type is not (ScenarioStepDefinition.CompleteTask or ScenarioStepDefinition.ExpectTask) || entity is null)
        {
            return;
        }

        var taskName = step.Step!;
        var tasks = processes
            .Where(process => ReferenceEquals(findEntity(process.Entity), entity))
            .SelectMany(process => process.Steps)
            .Where(candidate => candidate.Type == ProcessStepDefinition.TaskStep && Same(candidate.Name, taskName))
            .ToList();
        if (tasks.Count == 0)
        {
            report(
                DiagnosticCodes.UnknownScenarioStep,
                $"The task step '{taskName}' was not found. No process over the entity '{entity.Name}' has a task step with that name.",
                $"{path}/step");
        }
        else if (step.Type == ScenarioStepDefinition.CompleteTask
            && step.Outcome is { } outcome
            && !tasks.Any(task => task.Outcomes?.Any(candidate => Same(candidate.Name, outcome)) == true))
        {
            report(
                DiagnosticCodes.UnknownScenarioOutcome,
                $"The outcome '{outcome}' was not found. The task step '{taskName}' has no outcome with that name.",
                $"{path}/outcome");
        }
    }

    /// <summary>
    /// Checks <paramref name="values"/> against <paramref name="entity"/>: each key names a field and
    /// each value fits the field's type. A child collection is an array of objects, checked against
    /// the child entity.
    /// </summary>
    private static void CheckValues(
        EntityResource entity,
        IEnumerable<KeyValuePair<string, JsonElement>> values,
        string path,
        Func<string, EntityResource?> findEntity,
        Action<string, string, string> report)
    {
        foreach (var (key, value) in values)
        {
            var valuePath = $"{path}/{Escape(key)}";
            var field = entity.Fields.FirstOrDefault(candidate => Same(candidate.Name, key));
            if (field is null)
            {
                report(
                    DiagnosticCodes.UnknownScenarioField,
                    $"The field '{key}' was not found. The entity '{entity.Name}' has no field with that name.",
                    valuePath);
                continue;
            }

            if (value.ValueKind == JsonValueKind.Null || !FieldTypes.TryParse(field.Type, out var type))
            {
                continue;
            }

            if (type == FieldType.ChildCollection)
            {
                CheckCollection(field, value, valuePath, findEntity, report);
            }
            else if (!FieldValues.TryRead(type, field.Values, value, out _))
            {
                var allowed = type == FieldType.Enum && field.Values is { Count: > 0 } ? $" Use one of: {string.Join(", ", field.Values)}." : "";
                report(
                    DiagnosticCodes.InvalidScenarioValue,
                    $"The value is not a valid {field.Type} for the field '{field.Name}'.{allowed}",
                    valuePath);
            }
        }
    }

    private static void CheckCollection(
        FieldDefinition field,
        JsonElement value,
        string path,
        Func<string, EntityResource?> findEntity,
        Action<string, string, string> report)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            report(
                DiagnosticCodes.InvalidScenarioValue,
                $"The value is not a valid {field.Type} for the field '{field.Name}'. Use an array of objects, one for each item.",
                path);
            return;
        }

        var child = field.Target is { } target ? findEntity(target) : null;
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{path}/{index++}";
            if (item.ValueKind != JsonValueKind.Object)
            {
                report(
                    DiagnosticCodes.InvalidScenarioValue,
                    $"An item of the field '{field.Name}' must be an object that holds the values of one item.",
                    itemPath);
            }
            else if (child is not null)
            {
                CheckValues(child, item.EnumerateObject().Select(property => KeyValuePair.Create(property.Name, property.Value)), itemPath, findEntity, report);
            }
        }
    }

    /// <summary>
    /// Checks a stub: a connector stub names a loaded connector and an operation of it, a package stub
    /// names a pinned package and an operation of its manifest. When the operation resolves, each output
    /// names an output field of it.
    /// </summary>
    private static void CheckStub(
        ScenarioStubDefinition stub,
        string path,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        Action<string, string, string> report)
    {
        IReadOnlyList<string>? outputs = null;
        if (stub.Connector is { } reference)
        {
            if (findConnector(reference.Name) is not { } connector)
            {
                if (!unloadedConnectorNames.Contains(reference.Name))
                {
                    report(
                        DiagnosticCodes.UnknownStubOperation,
                        $"The connector '{reference.Name}' was not found. No loaded connector has that name.",
                        $"{path}/connector/name");
                }
            }
            else if (ProcessChecker.FindOperation(connector, reference.Operation) is not { } operation)
            {
                report(
                    DiagnosticCodes.UnknownStubOperation,
                    $"The operation '{reference.Operation}' was not found. The connector '{connector.Name}' has no operation with that name.",
                    $"{path}/connector/operation");
            }
            else
            {
                outputs = [.. operation.Output.Select(field => field.Name)];
            }
        }
        else
        {
            // The schema requires a connector or an operation, and an operation has a package id and a name.
            var separator = stub.Operation!.IndexOf('/', StringComparison.Ordinal);
            var packageId = stub.Operation[..separator];
            var operationName = stub.Operation[(separator + 1)..];
            if (findPackage(packageId) is not { } package)
            {
                // A listed package that failed pinning is already reported.
                if (declaredPackageIds is not null && !declaredPackageIds.Contains(packageId))
                {
                    report(
                        DiagnosticCodes.UnknownStubOperation,
                        $"The package '{packageId}' is not one of the application's packages. Add it to 'packages' in application.json.",
                        $"{path}/operation");
                }
            }
            else if (ProcessChecker.FindPackageOperation(package, operationName) is not { } operation)
            {
                report(
                    DiagnosticCodes.UnknownStubOperation,
                    $"The operation '{operationName}' was not found. Package '{package.Id}' {package.Version} has no operation with that name.",
                    $"{path}/operation");
            }
            else
            {
                outputs = [.. operation.Output.Select(field => field.Name)];
            }
        }

        if (outputs is null)
        {
            return;
        }

        foreach (var key in stub.Output.Keys)
        {
            if (!outputs.Any(name => Same(name, key)))
            {
                report(
                    DiagnosticCodes.UnknownStubOutput,
                    $"The output '{key}' was not found. The operation has no output field with that name.",
                    $"{path}/output/{Escape(key)}");
            }
        }
    }

    /// <summary>Escapes a property name as an RFC 6901 JSON Pointer segment.</summary>
    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
