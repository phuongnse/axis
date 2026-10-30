using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the triggers of an application and builds the checked ones. A trigger names a loaded
/// process, which may be a process of a module that the application declares, as <c>module/Name</c>.
/// A name under a module that the application does not declare is <c>AXC0207</c>. An event trigger,
/// in async or sync mode, names a loaded event about the process's subject entity, and its <c>when</c> is a boolean that sees only the event's payload fields. A data
/// change trigger names the process's subject entity, any <c>fields</c> are non-computed fields of it,
/// and its <c>when</c> is a boolean over the watched record with the scope of a process condition.
/// A delete trigger lists only <c>delete</c> in <c>on</c>, has no <c>fields</c> and names a process
/// without a subject, and its parameters give each process parameter an expression over the deleted
/// record. Every problem of a trigger is reported in one pass. A process, event or entity whose file did
/// not load is not reported again, the entity match is not checked when either entity is unknown,
/// and <c>when</c> is not checked without the event or the entity. A schedule trigger has a valid cron
/// and a known IANA time zone, and names a loaded data source that is not grouped and is over the
/// process's subject entity. Its parameters give a literal value of its type to every parameter of the
/// data source and name no other. A schedule trigger without a data source is a scheduled job. It names
/// a process without a subject, and its parameters give a literal value of its type to every parameter
/// of the process and name no other. A module trigger is checked inside its module, see
/// <see cref="ApplicationCompiler"/>: its process is a process of its own module, and its event or entity is
/// one of its own module or of a module it declares, so it sees no application data source and no application
/// rule, only the rules of its module. A module schedule trigger has no time zone and runs in the time zone
/// of the application. It names a data source of its own module, or none.
/// </summary>
internal static class TriggerChecker
{
    /// <summary>Checks <paramref name="triggers"/>, which come in path order.</summary>
    public static void Check(
        IReadOnlyList<TriggerResource> triggers,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> unloadedDataSourceNames,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        foreach (var trigger in triggers)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, trigger.File, path, trigger.Id));

            var process = moduleReferences.Find(trigger.Process, findProcess);
            if (process is not null)
            {
                references.Add(trigger, "/process", ReferenceRelations.TriggerProcess, ResourceNode.Of(process));
            }
            else if (!unloadedProcessNames.Contains(trigger.Process) && !moduleReferences.ReportUndeclared(trigger.Process, "/process", Report))
            {
                // A process file that was not loaded because of its own errors is not reported again.
                Report(
                    DiagnosticCodes.UnknownTriggerProcess,
                    $"The process '{trigger.Process}' was not found. No loaded process has that name.",
                    "/process");
            }

            // A scheduled job, a schedule trigger without a data source, starts a process without a subject.
            if (process is { Entity: null }
                && !(trigger.Type == "schedule" && trigger.DataSource is null)
                && !IsDelete(trigger))
            {
                Report(
                    DiagnosticCodes.TriggerProcessWithoutSubject,
                    $"The process '{process.Name}' has no subject, so a trigger cannot start it. A trigger starts the process for a record.",
                    "/process");
            }

            if (trigger.Type == "dataChange")
            {
                CheckDataChange(trigger, process, findEntity, unloadedEntityNames, rules, modules, references, Report);
                continue;
            }

            if (trigger.Type == "schedule")
            {
                CheckSchedule(trigger, process, findEntity, findDataSource, unloadedDataSourceNames, references, Report);
                continue;
            }

            // The schema requires an event for an event trigger.
            var eventName = trigger.Event!;
            var @event = findEvent(eventName);
            if (@event is null)
            {
                if (!unloadedEventNames.Contains(eventName))
                {
                    Report(
                        DiagnosticCodes.UnknownTriggerEvent,
                        $"The event '{eventName}' was not found. No loaded event has that name.",
                        "/event");
                }

                // The condition cannot be checked without the event's payload.
                continue;
            }

            references.Add(trigger, "/event", ReferenceRelations.TriggerEvent, ResourceNode.Of(@event));

            // An unknown entity has a diagnostic of its own on the event or the process.
            if (process is not null
                && findEntity(@event.Entity) is { } eventEntity
                && process.Entity is { } subjectName
                && findEntity(subjectName) is { } subject
                && !ReferenceEquals(eventEntity, subject))
            {
                Report(
                    DiagnosticCodes.TriggerEventOverOtherEntity,
                    $"The event '{@event.Name}' is about the entity '{eventEntity.Name}', but the process '{process.Name}' is over '{subject.Name}'. A trigger starts the process for the record the event names.",
                    "/event");
            }

            if (trigger.When is { } when)
            {
                var parsed = ExpressionParser.Parse(when);
                var check = parsed.Succeeded
                    ? ExpressionTypeChecker.Check(parsed.Expression!, ExpressionScopes.ForEventPayload(@event.Fields), ExpressionType.Boolean)
                    : null;
                var problem = parsed.Succeeded ? check!.Diagnostic : parsed.Diagnostic;
                references.RecordExpression(trigger, "/when", check?.Type, problem);
                if (problem is not null)
                {
                    Report(problem.Code, problem.Message, "/when");
                }
            }
        }
    }

    /// <summary>Whether the trigger is a data change trigger that lists <c>delete</c>.</summary>
    private static bool IsDelete(TriggerResource trigger) =>
        trigger.Type == "dataChange" && trigger.On!.Contains("delete");

    /// <summary>
    /// Checks a data change trigger. The schema requires its entity and <c>on</c>. An unknown
    /// entity, or one that is not the process's subject, stops the check of the trigger's fields
    /// and condition. A delete trigger is checked against a process without a subject instead, and
    /// its parameters are checked with its condition.
    /// </summary>
    private static void CheckDataChange(
        TriggerResource trigger,
        ProcessResource? process,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> unloadedEntityNames,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var entityName = trigger.Entity!;
        var entity = findEntity(entityName);
        if (entity is null)
        {
            if (!unloadedEntityNames.Contains(entityName))
            {
                report(
                    DiagnosticCodes.UnknownTriggerEntity,
                    $"The entity '{entityName}' was not found. No loaded entity has that name.",
                    "/entity");
            }

            return;
        }

        references.Add(trigger, "/entity", ReferenceRelations.TriggerEntity, ResourceNode.Of(entity));

        var isDelete = IsDelete(trigger);
        if (isDelete)
        {
            if (trigger.On!.Any(kind => kind != "delete"))
            {
                report(
                    DiagnosticCodes.DeleteTriggerWithOtherKinds,
                    "A delete trigger lists only 'delete' in 'on'. A record that is deleted has no later state for a create or an update trigger to start a process for.",
                    "/on");
            }

            if (trigger.Fields is not null)
            {
                report(
                    DiagnosticCodes.DeleteTriggerWithFields,
                    "A delete trigger has no 'fields'. A delete removes the whole record, so no field changes.",
                    "/fields");
            }

            if (process is { Entity: not null })
            {
                report(
                    DiagnosticCodes.DeleteTriggerProcessWithSubject,
                    $"The process '{process.Name}' has a subject, so a delete trigger cannot start it. The record is gone, so name a process without a subject.",
                    "/process");
            }
        }

        // An unknown entity has a diagnostic of its own on the process.
        if (!isDelete
            && process is not null
            && process.Entity is { } subjectName
            && findEntity(subjectName) is { } subject
            && !ReferenceEquals(entity, subject))
        {
            report(
                DiagnosticCodes.TriggerEntityNotProcessSubject,
                $"The trigger watches the entity '{entity.Name}', but the process '{process.Name}' is over '{subject.Name}'. A trigger starts the process for the record that changed.",
                "/entity");
        }

        // The per-field checks do not apply to a delete trigger, which has no fields.
        var fields = isDelete ? [] : trigger.Fields ?? [];
        for (var index = 0; index < fields.Count; index++)
        {
            var field = FindField(entity, fields[index]);
            if (field is null)
            {
                report(
                    DiagnosticCodes.InvalidTriggerField,
                    $"The field '{fields[index]}' names no field of the entity '{entity.Name}'.",
                    $"/fields/{index}");
            }
            else if (field.Expression is not null)
            {
                report(
                    DiagnosticCodes.InvalidTriggerField,
                    $"The field '{field.Name}' is a computed field, which no write changes.",
                    $"/fields/{index}");
            }
        }

        if (trigger.When is { } when)
        {
            var parsed = ExpressionParser.Parse(when);
            ExpressionDiagnostic? problem;
            ExpressionType? type = null;
            if (parsed.Succeeded)
            {
                var check = ExpressionTypeChecker.Check(
                    parsed.Expression!, ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules), ExpressionType.Boolean);
                if (check.Succeeded)
                {
                    references.AddRuleCalls(trigger, "/when", check);
                }

                problem = check.Diagnostic;
                type = check.Type;
            }
            else
            {
                problem = parsed.Diagnostic;
            }

            references.RecordExpression(trigger, "/when", type, problem);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, "/when");
            }
        }

        if (isDelete && process is { Entity: null })
        {
            CheckDeleteParameters(trigger, process, entity, rules, findEntity, modules, references, report);
        }
    }

    /// <summary>
    /// Checks the parameters of a delete trigger against the process's. Each value is an expression over
    /// the deleted record, of the type of the parameter it gives. A process that is not loaded has a
    /// diagnostic of its own.
    /// </summary>
    private static void CheckDeleteParameters(
        TriggerResource trigger,
        ProcessResource process,
        EntityResource entity,
        IEnumerable<ExpressionRule> rules,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> modules,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var definitions = process.Parameters ?? [];
        var given = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in trigger.Parameters ?? new Dictionary<string, JsonElement>())
        {
            var path = $"/parameters/{key}";
            var definition = definitions.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{key}' was not found. The process '{process.Name}' has no parameter with that name.",
                    path);
                continue;
            }

            if (!given.TryAdd(definition.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{definition.Name}' is already given by '{given[definition.Name]}'.",
                    path);
                continue;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                report(
                    DiagnosticCodes.InvalidTriggerParameterValue,
                    $"The value for the parameter '{definition.Name}' is not an expression. Give a string that is an expression over the deleted record.",
                    path);
                continue;
            }

            var parsed = ExpressionParser.Parse(value.GetString()!);
            ExpressionDiagnostic? problem;
            ExpressionType? type = null;
            if (parsed.Succeeded)
            {
                // A parameter whose type is unknown has a diagnostic of its own on the process.
                if (!FieldTypes.TryParse(definition.Type, out var parameterType))
                {
                    continue;
                }

                var check = ExpressionTypeChecker.Check(
                    parsed.Expression!,
                    ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules),
                    ExpressionScopes.TypeOf(parameterType));
                if (check.Succeeded)
                {
                    references.AddRuleCalls(trigger, path, check);
                }

                problem = check.Diagnostic;
                type = check.Type;
            }
            else
            {
                problem = parsed.Diagnostic;
            }

            references.RecordExpression(trigger, path, type, problem);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, path);
            }
        }

        foreach (var definition in definitions)
        {
            if (!given.ContainsKey(definition.Name))
            {
                report(
                    DiagnosticCodes.MissingTriggerParameter,
                    $"The parameter '{definition.Name}' is a parameter of the process '{process.Name}', but the trigger does not give it. Give an expression over the deleted record.",
                    "/parameters");
            }
        }
    }

    /// <summary>
    /// Checks a schedule trigger in one pass. The schema requires its cron and time zone. An unknown
    /// data source stops the check of its parameters. Without a data source the trigger is a scheduled
    /// job, and its parameters are checked against the process's.
    /// </summary>
    private static void CheckSchedule(
        TriggerResource trigger,
        ProcessResource? process,
        Func<string, EntityResource?> findEntity,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> unloadedDataSourceNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (!CronSchedule.TryParse(trigger.Schedule!, out _, out var cronError))
        {
            report(DiagnosticCodes.InvalidTriggerSchedule, $"The schedule '{trigger.Schedule}' is not valid. {cronError}", "/schedule");
        }

        // An application trigger always has a time zone, the schema requires it. A module trigger has none.
        if (trigger.TimeZone is { } timeZoneId && !IanaTimeZones.TryFind(timeZoneId, out _))
        {
            report(
                DiagnosticCodes.UnknownTriggerTimeZone,
                $"The time zone '{timeZoneId}' is not known. Use an IANA id such as 'Europe/Berlin', written with its exact letter case.",
                "/timeZone");
        }

        if (trigger.DataSource is null)
        {
            CheckJobParameters(trigger, process, report);
            return;
        }

        var dataSource = findDataSource(trigger.DataSource);
        if (dataSource is null)
        {
            if (!unloadedDataSourceNames.Contains(trigger.DataSource))
            {
                report(
                    DiagnosticCodes.UnknownTriggerDataSource,
                    $"The data source '{trigger.DataSource}' was not found. No loaded data source has that name.",
                    "/dataSource");
            }

            return;
        }

        references.Add(trigger, "/dataSource", ReferenceRelations.TriggerDataSource, ResourceNode.Of(dataSource));

        if (dataSource.Aggregate is not null)
        {
            report(
                DiagnosticCodes.GroupedTriggerDataSource,
                $"The data source '{dataSource.Name}' is grouped. A schedule starts the process for each root record, so its data source has one row for each record.",
                "/dataSource");
        }

        // An unknown entity has a diagnostic of its own on the data source or the process.
        if (process is not null
            && findEntity(dataSource.Entity) is { } rowEntity
            && process.Entity is { } subjectName
            && findEntity(subjectName) is { } subject
            && !ReferenceEquals(rowEntity, subject))
        {
            report(
                DiagnosticCodes.TriggerDataSourceOverOtherEntity,
                $"The data source '{dataSource.Name}' is over the entity '{rowEntity.Name}', but the process '{process.Name}' is over '{subject.Name}'. A schedule starts the process for each row of the data source.",
                "/dataSource");
        }

        var given = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in trigger.Parameters ?? new Dictionary<string, JsonElement>())
        {
            var path = $"/parameters/{key}";
            var parameter = FindParameter(dataSource, key);
            if (parameter is null)
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{key}' was not found. The data source '{dataSource.Name}' has no parameter with that name.",
                    path);
                continue;
            }

            if (!given.TryAdd(parameter.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{parameter.Name}' is already given by '{given[parameter.Name]}'.",
                    path);
                continue;
            }

            if (!TryReadParameterValue(parameter, value, out _))
            {
                report(
                    DiagnosticCodes.InvalidTriggerParameterValue,
                    value.ValueKind == JsonValueKind.Null
                        ? $"The parameter '{parameter.Name}' is required, so its value cannot be null."
                        : $"The value is not a valid {parameter.Type} for the parameter '{parameter.Name}'.",
                    path);
            }
        }

        foreach (var parameter in dataSource.Parameters)
        {
            if (!given.ContainsKey(parameter.Name))
            {
                report(
                    DiagnosticCodes.MissingTriggerParameter,
                    $"The parameter '{parameter.Name}' is a parameter of the data source '{dataSource.Name}', but the trigger does not give it. Give a value, or null for an optional parameter.",
                    "/parameters");
            }
        }
    }

    /// <summary>
    /// Checks the parameters of a scheduled job against the process's. A process that is not loaded
    /// has a diagnostic of its own, and a process with a subject is reported once, without its
    /// parameters.
    /// </summary>
    private static void CheckJobParameters(TriggerResource trigger, ProcessResource? process, Action<string, string, string> report)
    {
        if (process is null)
        {
            return;
        }

        if (process.Entity is not null)
        {
            report(
                DiagnosticCodes.ScheduleJobProcessWithSubject,
                $"The process '{process.Name}' has a subject, so a schedule trigger without a data source cannot start it. Name a process without a subject, or give a dataSource.",
                "/process");
            return;
        }

        var definitions = process.Parameters ?? [];
        var given = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in trigger.Parameters ?? new Dictionary<string, JsonElement>())
        {
            var path = $"/parameters/{key}";
            var definition = definitions.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{key}' was not found. The process '{process.Name}' has no parameter with that name.",
                    path);
                continue;
            }

            if (!given.TryAdd(definition.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownTriggerParameter,
                    $"The parameter '{definition.Name}' is already given by '{given[definition.Name]}'.",
                    path);
                continue;
            }

            if (!TryReadProcessParameterValue(definition, value, out _))
            {
                report(
                    DiagnosticCodes.InvalidTriggerParameterValue,
                    value.ValueKind == JsonValueKind.Null
                        ? $"The parameter '{definition.Name}' is required, so its value cannot be null."
                        : $"The value is not a valid {definition.Type} for the parameter '{definition.Name}'.",
                    path);
            }
        }

        foreach (var definition in definitions)
        {
            if (!given.ContainsKey(definition.Name))
            {
                report(
                    DiagnosticCodes.MissingTriggerParameter,
                    $"The parameter '{definition.Name}' is a parameter of the process '{process.Name}', but the trigger does not give it. Give a value.",
                    "/parameters");
            }
        }
    }

    private static DataSourceParameterDefinition? FindParameter(DataSourceResource dataSource, string name) =>
        dataSource.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the JSON <paramref name="element"/> as a value of <paramref name="parameter"/>'s type, in the
    /// record API's JSON forms: a number for integer and decimal, true or false for boolean and a string for
    /// the other types. The CLR value is the one the data source request gives. Null is allowed only for an
    /// optional parameter.
    /// </summary>
    private static bool TryReadParameterValue(DataSourceParameterDefinition parameter, JsonElement element, out object? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return parameter.Required != true;
        }

        return FieldTypes.TryParse(parameter.Type, out var type) && FieldValues.TryRead(type, parameter.Values, element, out value);
    }

    /// <summary>
    /// Reads the JSON <paramref name="element"/> as a value of a process parameter's type, in the same
    /// JSON forms. A process parameter has no optional flag, so null is not a value.
    /// </summary>
    private static bool TryReadProcessParameterValue(ProcessParameterDefinition parameter, JsonElement element, out object? value)
    {
        value = null;
        return element.ValueKind != JsonValueKind.Null
            && FieldTypes.TryParse(parameter.Type, out var type)
            && FieldValues.TryRead(type, null, element, out value);
    }

    private static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a checked trigger. Its event or entity and process are resolved and named as they
    /// declare themselves. Its <c>when</c> is compiled over the event's payload fields, or for a
    /// data change trigger over the watched record with the scope of a process condition. A schedule
    /// trigger without a time zone, a module's, takes <paramref name="applicationTimeZone"/>.
    /// </summary>
    public static TriggerModel Build(
        TriggerResource trigger,
        Func<string, EntityResource?> findEntity,
        Func<string, EventResource?> findEvent,
        Func<string, ProcessResource?> findProcess,
        Func<string, DataSourceResource?> findDataSource,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules,
        TimeZoneInfo applicationTimeZone)
    {
        if (trigger.Type == "dataChange")
        {
            return BuildDataChange(trigger, findEntity, findProcess, rules, modules);
        }

        if (trigger.Type == "schedule")
        {
            return BuildSchedule(trigger, findProcess, findDataSource, applicationTimeZone);
        }

        var @event = findEvent(trigger.Event!)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,

            // A trigger without a mode is async.
            Type = TriggerType.Event,
            Mode = trigger.Mode == "sync" ? TriggerMode.Sync : TriggerMode.Async,
            Event = new EventReference(@event.Id, @event.Name),
            Process = new ProcessReference(process.Id, process.Name),
            When = trigger.When is { } when
                ? ExpressionModel.Compile(when, ExpressionScopes.ForEventPayload(@event.Fields), ExpressionType.Boolean)
                : null,
        };
    }

    private static TriggerModel BuildSchedule(
        TriggerResource trigger,
        Func<string, ProcessResource?> findProcess,
        Func<string, DataSourceResource?> findDataSource,
        TimeZoneInfo applicationTimeZone)
    {
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var dataSource = trigger.DataSource is { } dataSourceName
            ? findDataSource(dataSourceName) ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger))
            : null;
        var timeZone = applicationTimeZone;
        if (!CronSchedule.TryParse(trigger.Schedule!, out var schedule, out _)
            || (trigger.TimeZone is { } timeZoneId && !IanaTimeZones.TryFind(timeZoneId, out timeZone)))
        {
            throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        }

        var given = trigger.Parameters ?? new Dictionary<string, JsonElement>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (dataSource is null)
        {
            // A scheduled job holds the values of the process's parameters.
            foreach (var parameter in process.Parameters ?? [])
            {
                var element = given.First(pair => string.Equals(pair.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value;
                if (!TryReadProcessParameterValue(parameter, element, out var value))
                {
                    throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
                }

                parameters.Add(parameter.Name, value);
            }
        }
        else
        {
            foreach (var parameter in dataSource.Parameters)
            {
                var element = given.First(pair => string.Equals(pair.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value;
                if (!TryReadParameterValue(parameter, element, out var value))
                {
                    throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
                }

                parameters.Add(parameter.Name, value);
            }
        }

        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,
            Type = TriggerType.Schedule,

            // The mode applies to event triggers only. A schedule trigger is async.
            Mode = TriggerMode.Async,
            Process = new ProcessReference(process.Id, process.Name),
            Schedule = schedule,
            TimeZone = timeZone,
            DataSource = dataSource is null ? null : new DataSourceReference(dataSource.Id, dataSource.Name),
            Parameters = parameters,
        };
    }

    private static TriggerModel BuildDataChange(
        TriggerResource trigger,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules)
    {
        var entity = findEntity(trigger.Entity!)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var on = DataChangeKinds.None;
        foreach (var kind in trigger.On!)
        {
            on |= kind switch
            {
                "create" => DataChangeKinds.Create,
                "delete" => DataChangeKinds.Delete,
                _ => DataChangeKinds.Update,
            };
        }

        // A delete trigger gives each parameter of the process an expression over the deleted record.
        Dictionary<string, ExpressionModel>? parameterExpressions = null;
        if (on.HasFlag(DataChangeKinds.Delete))
        {
            var scope = ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules);
            var given = trigger.Parameters ?? new Dictionary<string, JsonElement>();
            parameterExpressions = new Dictionary<string, ExpressionModel>(StringComparer.Ordinal);
            foreach (var parameter in process.Parameters ?? [])
            {
                var text = given.First(pair => string.Equals(pair.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value.GetString()!;
                parameterExpressions.Add(parameter.Name, ExpressionModel.Compile(text, scope, ExpressionScopes.TypeOf(parameter.Type)));
            }
        }

        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,
            Type = TriggerType.DataChange,

            // The mode applies to event triggers only. A data change trigger is async.
            Mode = TriggerMode.Async,
            Entity = new EntityReference(entity.Id, entity.Name),
            On = on,
            Fields = trigger.Fields?.Select(name => FindField(entity, name)!.Name).ToList(),
            Process = new ProcessReference(process.Id, process.Name),
            When = trigger.When is { } when
                ? ExpressionModel.Compile(when, ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules), ExpressionType.Boolean)
                : null,
            ParameterExpressions = parameterExpressions,
        };
    }
}
