using System.Globalization;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the contracts of the pinned modules and returns the ones without a problem, by the qualified
/// name of their entity. A contract names a plain-named, non-child entity of its own module, and a
/// module has at most one contract for each entity. A contract has at least one of its four parts. The
/// lifecycle is checked: its state field is a required, plain, non-computed <c>enum</c>, its
/// states are the field's values, one is initial, and each transition names states of the lifecycle. A
/// guard is a boolean expression in the entity's validation scope with the module's rules, and a
/// message text key starts with the module's prefix. The ceiling is checked too: each key of its
/// <c>slots</c> is a role slot of the module, each entry field is a field of the entity, and each entry
/// filter type-checks as a policy filter in the scope of a module policy filter. A grant that goes past
/// the ceiling is checked later, by <see cref="GrantCeilings"/>. Each core validation has a name that is
/// unique ignoring letter case, a field of the entity, a message key with the module's prefix and a boolean
/// expression in the same scope as a guard. The effects are checked too: each name is unique, ignoring letter
/// case, <c>on</c> names a transition of the contract exactly, <c>create.entity</c> is a plain name of a root
/// entity of the module, and each <c>values</c> key is a writable field of it with an expression that fits the
/// field in the effect scope. Every problem of a contract is reported in one pass.
/// </summary>
internal static class ContractChecker
{
    /// <summary>
    /// Checks <paramref name="contracts"/>, which come in path order. <paramref name="ownerOf"/> names the
    /// owner of a child entity, or gives null for a root entity. <paramref name="rulesFor"/> and
    /// <paramref name="modulesFor"/> give the rules and the declared module ids a contract's expressions see.
    /// <paramref name="moduleSlots"/> holds the role slot names of each pinned module, and
    /// <paramref name="filterRulesFor"/> gives the rules a ceiling filter sees.
    /// </summary>
    public static Dictionary<string, ContractResource> Check(
        IReadOnlyList<ModuleContract> contracts,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<Resource, IReadOnlyList<ExpressionRule>> rulesFor,
        Func<Resource, IReadOnlySet<string>> modulesFor,
        IReadOnlyDictionary<string, IReadOnlySet<string>> moduleSlots,
        Func<Resource, IReadOnlyList<ExpressionRule>> filterRulesFor,
        List<Diagnostic> diagnostics)
    {
        var checkedContracts = new Dictionary<string, ContractResource>(StringComparer.OrdinalIgnoreCase);
        var firstFileByEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (moduleId, contract) in contracts)
        {
            var before = diagnostics.Count;
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, contract.File, path, contract.Id));

            if (CheckEntity(moduleId, contract, findEntity, ownerOf, unloadedEntityNames, firstFileByEntity, Report) is { } entity)
            {
                if (contract.Lifecycle is { } lifecycle)
                {
                    CheckLifecycle(moduleId, contract, lifecycle, entity, findEntity, rulesFor, modulesFor, Report);
                }

                if (contract.Validations is { } validations)
                {
                    CheckValidations(moduleId, contract, validations, entity, findEntity, rulesFor, modulesFor, Report);
                }

                if (contract.Effects is { } effects)
                {
                    CheckEffects(moduleId, contract, effects, entity, findEntity, ownerOf, unloadedEntityNames, rulesFor, modulesFor, Report);
                }

                if (contract.Ceiling is { } ceiling)
                {
                    CheckCeiling(
                        ceiling,
                        entity,
                        findEntity,
                        moduleSlots.GetValueOrDefault(moduleId) ?? new HashSet<string>(),
                        filterRulesFor(contract),
                        modulesFor(contract),
                        Report);
                }

                if (diagnostics.Count == before)
                {
                    checkedContracts[entity.Name] = contract;
                }
            }
        }

        return checkedContracts;
    }

    private static void CheckEffects(
        string moduleId,
        ContractResource contract,
        IReadOnlyList<ContractEffectDefinition> effects,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<Resource, IReadOnlyList<ExpressionRule>> rulesFor,
        Func<Resource, IReadOnlySet<string>> modulesFor,
        Action<string, string, string> report)
    {
        var transitions = contract.Lifecycle?.Transitions ?? [];
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ExpressionScope? scope = null;
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var path = $"/effects/{index.ToString(CultureInfo.InvariantCulture)}";

            if (!firstIndexByName.TryAdd(effect.Name, index))
            {
                report(
                    DiagnosticCodes.DuplicateContractName,
                    $"The effect '{effect.Name}' repeats the effect at index {firstIndexByName[effect.Name].ToString(CultureInfo.InvariantCulture)}. Effect names are unique, ignoring letter case.",
                    $"{path}/name");
            }

            if (!transitions.Any(transition => string.Equals(transition.Name, effect.On, StringComparison.Ordinal)))
            {
                report(
                    DiagnosticCodes.UnknownEffectTransition,
                    contract.Lifecycle is null
                        ? $"The effect '{effect.Name}' runs on the transition '{effect.On}', but the contract has no lifecycle."
                        : $"'{effect.On}' is not a transition of the contract. The transitions are {(transitions.Count == 0 ? "none" : string.Join(", ", transitions.Select(transition => $"'{transition.Name}'")))}.",
                    $"{path}/on");
            }

            if (CheckEffectEntity(moduleId, effect, findEntity, ownerOf, unloadedEntityNames, $"{path}/create/entity", report) is not { } target)
            {
                continue;
            }

            scope ??= ExpressionScopes.ForContractEffect(entity.Name, entity.Fields, rulesFor(contract), findEntity, modulesFor(contract));
            CheckEffectValues(effect, target, scope, $"{path}/create/values", report);
        }
    }

    /// <summary>Finds the entity an effect creates and checks it is a plain name of a root entity of the module. Gives null when it is not.</summary>
    private static EntityResource? CheckEffectEntity(
        string moduleId,
        ContractEffectDefinition effect,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        string path,
        Action<string, string, string> report)
    {
        var name = effect.Create.Entity;
        if (name.Contains('/', StringComparison.Ordinal))
        {
            report(
                DiagnosticCodes.InvalidEffectEntity,
                $"The entity '{name}' is not a plain name. An effect creates a record of an entity of its own module, named without '/'.",
                path);
            return null;
        }

        var qualified = $"{moduleId}/{name}";
        if (findEntity(qualified) is not { } target)
        {
            // An entity file that was not loaded because of its own errors is not reported again.
            if (!unloadedEntityNames.Contains(qualified))
            {
                report(DiagnosticCodes.InvalidEffectEntity, $"The module '{moduleId}' has no entity '{name}'.", path);
            }

            return null;
        }

        if (ownerOf(target.Name) is { } owner)
        {
            report(
                DiagnosticCodes.InvalidEffectEntity,
                $"The entity '{target.Name}' is a child entity owned by '{owner}'. An effect creates a record of a root entity.",
                path);
            return null;
        }

        return target;
    }

    /// <summary>
    /// Checks each <c>values</c> key of an effect: it names a field of <paramref name="target"/>, ignoring
    /// letter case, that no earlier key names and that an effect can set. Its expression then fits the
    /// field's type over <paramref name="scope"/>.
    /// </summary>
    private static void CheckEffectValues(
        ContractEffectDefinition effect,
        EntityResource target,
        ExpressionScope scope,
        string path,
        Action<string, string, string> report)
    {
        // The key that first set each field, by the field's declared name.
        var setBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in effect.Create.Values)
        {
            var valuePath = $"{path}/{ModuleChecker.Escape(key)}";
            var field = ApplicationCompiler.FindField(target, key);
            if (field is null)
            {
                report(
                    DiagnosticCodes.InvalidEffectValue,
                    $"The field '{key}' was not found. The entity '{target.Name}' has no field with that name.",
                    valuePath);
                continue;
            }

            if (!setBy.TryAdd(field.Name, key))
            {
                report(
                    DiagnosticCodes.InvalidEffectValue,
                    $"The field '{field.Name}' is already set by '{setBy[field.Name]}' in this effect.",
                    valuePath);
                continue;
            }

            var reason = ProcessChecker.ReadOnlyReason(field) ?? (field.Creator == true ? "is set to its creator" : null);
            if (reason is not null)
            {
                report(DiagnosticCodes.InvalidEffectValue, $"The field '{field.Name}' {reason}, so an effect cannot set it.", valuePath);
                continue;
            }

            var parsed = ExpressionParser.Parse(expression);
            var problem = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionScopes.TypeOf(field)!, contract: true).Diagnostic
                : parsed.Diagnostic;
            if (problem is not null)
            {
                report(problem.Code, problem.Message, valuePath);
            }
        }
    }

    private static void CheckCeiling(
        ContractCeilingDefinition ceiling,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> slots,
        IReadOnlyList<ExpressionRule> filterRules,
        IReadOnlySet<string> modules,
        Action<string, string, string> report)
    {
        // A ceiling filter is a policy filter, so it is checked as a module policy filter of the same module is.
        ExpressionScope? scope = null;
        foreach (var (slot, entries) in ceiling.Slots)
        {
            var slotPath = $"/ceiling/slots/{ModuleChecker.Escape(slot)}";
            if (!slots.Contains(slot))
            {
                report(
                    DiagnosticCodes.UnknownCeilingSlot,
                    slots.Count == 0
                        ? $"The ceiling names the slot '{slot}', but the module has no role slots."
                        : $"'{slot}' is not a role slot of the module. The slots are {string.Join(", ", slots.Order(StringComparer.Ordinal).Select(name => $"'{name}'"))}.",
                    slotPath);
            }

            CheckEntries(entries, slotPath);
        }

        if (ceiling.Others is { } others)
        {
            CheckEntries(others, "/ceiling/others");
        }

        void CheckEntries(IReadOnlyList<CeilingEntryDefinition> entries, string path)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var entryPath = $"{path}/{index.ToString(CultureInfo.InvariantCulture)}";
                for (var fieldIndex = 0; fieldIndex < (entry.Fields?.Count ?? 0); fieldIndex++)
                {
                    var name = entry.Fields![fieldIndex];
                    if (ApplicationCompiler.FindField(entity, name) is null)
                    {
                        report(
                            DiagnosticCodes.UnknownCeilingField,
                            $"The field '{name}' is not a field of the entity '{entity.Name}'.",
                            $"{entryPath}/fields/{fieldIndex.ToString(CultureInfo.InvariantCulture)}");
                    }
                }

                if (entry.Filter is { } filter)
                {
                    scope ??= ExpressionScopes.ForDataSource(entity.Fields, [], findEntity, filterRules, modules, undeclaredModuleIsUnknown: true);
                    if (ApplicationCompiler.CheckFilter(filter, scope, policyFilter: true, entity, findEntity, out _) is { } problem)
                    {
                        report(problem.Code, problem.Message, $"{entryPath}/filter");
                    }
                }
            }
        }
    }

    /// <summary>Finds the contract's entity and checks the contract's shape. Gives null when the lifecycle cannot be checked.</summary>
    private static EntityResource? CheckEntity(
        string moduleId,
        ContractResource contract,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Dictionary<string, string> firstFileByEntity,
        Action<string, string, string> report)
    {
        if (contract.Lifecycle is null && contract.Validations is null && contract.Effects is null && contract.Ceiling is null)
        {
            report(
                DiagnosticCodes.EmptyContract,
                "A contract needs at least one of 'lifecycle', 'validations', 'effects' and 'ceiling'.",
                "");
        }

        if (contract.Entity.Contains('/', StringComparison.Ordinal))
        {
            report(
                DiagnosticCodes.UnknownContractEntity,
                $"The entity '{contract.Entity}' is not a plain name. A contract names an entity of its own module without '/'.",
                "/entity");
            return null;
        }

        var qualified = $"{moduleId}/{contract.Entity}";
        var entity = findEntity(qualified);
        if (entity is null)
        {
            // An entity file that was not loaded because of its own errors is not reported again.
            if (!unloadedEntityNames.Contains(qualified))
            {
                report(
                    DiagnosticCodes.UnknownContractEntity,
                    $"The module '{moduleId}' has no entity '{contract.Entity}'.",
                    "/entity");
            }

            return null;
        }

        if (ownerOf(entity.Name) is { } owner)
        {
            report(
                DiagnosticCodes.ContractOnChildEntity,
                $"The entity '{entity.Name}' is a child entity owned by '{owner}'. A contract covers an owner entity, and a child entity has none of its own.",
                "/entity");
            return null;
        }

        if (!firstFileByEntity.TryAdd(entity.Name, contract.File))
        {
            report(
                DiagnosticCodes.DuplicateContract,
                $"The entity '{entity.Name}' already has a contract in '{firstFileByEntity[entity.Name]}'. A module has at most one contract for each entity.",
                "/entity");
            return null;
        }

        return entity;
    }

    private static void CheckLifecycle(
        string moduleId,
        ContractResource contract,
        ContractLifecycleDefinition lifecycle,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        Func<Resource, IReadOnlyList<ExpressionRule>> rulesFor,
        Func<Resource, IReadOnlySet<string>> modulesFor,
        Action<string, string, string> report)
    {
        var field = ApplicationCompiler.FindField(entity, lifecycle.Field);
        var problem = FieldProblem(field);
        if (problem is not null)
        {
            report(
                DiagnosticCodes.InvalidLifecycleField,
                $"The state field '{lifecycle.Field}' {problem}. The state field is a required, non-computed enum field that is not sensitive.",
                "/lifecycle/field");
        }

        CheckStates(lifecycle, problem is null ? field!.Values ?? [] : null, report);
        CheckTransitions(moduleId, contract, lifecycle, entity, findEntity, rulesFor, modulesFor, report);
    }

    /// <summary>Says why a field cannot hold a state, or gives null when it can.</summary>
    private static string? FieldProblem(FieldDefinition? field) =>
        field is null ? "names no field of the entity"
        : FieldTypes.Parse(field.Type) != FieldType.Enum ? $"is a {field.Type} field, not an enum"
        : field.Required != true ? "is not required"
        : field.Expression is not null ? "is computed"
        : field.Sensitive == true ? "is sensitive"
        : null;

    /// <summary>Checks the states. <paramref name="values"/> is the state field's values, or null when the field is not valid.</summary>
    private static void CheckStates(
        ContractLifecycleDefinition lifecycle,
        IReadOnlyList<string>? values,
        Action<string, string, string> report)
    {
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lifecycle.States.Count; index++)
        {
            var name = lifecycle.States[index].Name;
            var path = $"/lifecycle/states/{index}/name";
            if (!firstIndexByName.TryAdd(name, index))
            {
                report(
                    DiagnosticCodes.DuplicateContractName,
                    $"The state '{name}' repeats the state at index {firstIndexByName[name].ToString(CultureInfo.InvariantCulture)}. State names are unique, ignoring letter case.",
                    path);
            }

            if (values is not null && !values.Contains(name, StringComparer.Ordinal))
            {
                report(
                    DiagnosticCodes.LifecycleStatesMismatch,
                    $"The state '{name}' is not a value of the field '{lifecycle.Field}'. A state is exactly one of its values.",
                    path);
            }
        }

        if (values is not null)
        {
            foreach (var value in values.Where(value => !lifecycle.States.Any(state => string.Equals(state.Name, value, StringComparison.Ordinal))))
            {
                report(
                    DiagnosticCodes.LifecycleStatesMismatch,
                    $"The value '{value}' of the field '{lifecycle.Field}' has no state. The states list every value of the field.",
                    "/lifecycle/states");
            }
        }

        if (!lifecycle.States.Any(state => state.Initial == true))
        {
            report(
                DiagnosticCodes.NoInitialState,
                "No state is initial. Mark at least one state 'initial', so a record can be created.",
                "/lifecycle/states");
        }
    }

    private static void CheckTransitions(
        string moduleId,
        ContractResource contract,
        ContractLifecycleDefinition lifecycle,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        Func<Resource, IReadOnlyList<ExpressionRule>> rulesFor,
        Func<Resource, IReadOnlySet<string>> modulesFor,
        Action<string, string, string> report)
    {
        var stateNames = lifecycle.States.Select(state => state.Name).ToHashSet(StringComparer.Ordinal);
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var transitionByPair = new Dictionary<(string From, string To), string>();
        ExpressionScope? scope = null;
        for (var index = 0; index < lifecycle.Transitions.Count; index++)
        {
            var transition = lifecycle.Transitions[index];
            var path = $"/lifecycle/transitions/{index}";

            if (!firstIndexByName.TryAdd(transition.Name, index))
            {
                report(
                    DiagnosticCodes.DuplicateContractName,
                    $"The transition '{transition.Name}' repeats the transition at index {firstIndexByName[transition.Name].ToString(CultureInfo.InvariantCulture)}. Transition names are unique, ignoring letter case.",
                    $"{path}/name");
            }

            var toKnown = stateNames.Contains(transition.To);
            if (!toKnown)
            {
                report(
                    DiagnosticCodes.UnknownTransitionState,
                    $"The state '{transition.To}' of the transition '{transition.Name}' is not a state of the lifecycle.",
                    $"{path}/to");
            }
            else if (transition.From.Contains(transition.To, StringComparer.Ordinal))
            {
                report(
                    DiagnosticCodes.TransitionToSameState,
                    $"The transition '{transition.Name}' enters '{transition.To}', which is also in 'from'. Staying in a state is no transition.",
                    $"{path}/to");
            }

            for (var from = 0; from < transition.From.Count; from++)
            {
                var state = transition.From[from];
                if (!stateNames.Contains(state))
                {
                    report(
                        DiagnosticCodes.UnknownTransitionState,
                        $"The state '{state}' of the transition '{transition.Name}' is not a state of the lifecycle.",
                        $"{path}/from/{from.ToString(CultureInfo.InvariantCulture)}");
                }
                else if (toKnown && !transitionByPair.TryAdd((state, transition.To), transition.Name))
                {
                    report(
                        DiagnosticCodes.DuplicateTransitionPair,
                        $"The transition '{transitionByPair[(state, transition.To)]}' already goes from '{state}' to '{transition.To}'. One transition at most belongs to each pair of states.",
                        $"{path}/from/{from.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            if (transition.Message is { } message && !message.TextKey.StartsWith($"{moduleId}.", StringComparison.Ordinal))
            {
                report(
                    DiagnosticCodes.ModuleTextKeyWithoutPrefix,
                    $"The text key '{message.TextKey}' lacks the prefix '{moduleId}.'. Start each text key of the module with it.",
                    $"{path}/message/textKey");
            }

            if (transition.Guard is { } guard)
            {
                scope ??= ExpressionScopes.ForEntity(
                    entity.Fields,
                    rules: rulesFor(contract),
                    findEntity: findEntity,
                    modules: modulesFor(contract),
                    undeclaredModuleIsUnknown: true);
                var parsed = ExpressionParser.Parse(guard);
                var problem = parsed.Succeeded ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean, contract: true).Diagnostic : parsed.Diagnostic;
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, $"{path}/guard");
                }
            }
        }
    }

    private static void CheckValidations(
        string moduleId,
        ContractResource contract,
        IReadOnlyList<ContractValidationDefinition> validations,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        Func<Resource, IReadOnlyList<ExpressionRule>> rulesFor,
        Func<Resource, IReadOnlySet<string>> modulesFor,
        Action<string, string, string> report)
    {
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ExpressionScope? scope = null;
        for (var index = 0; index < validations.Count; index++)
        {
            var validation = validations[index];
            var path = $"/validations/{index.ToString(CultureInfo.InvariantCulture)}";

            if (!firstIndexByName.TryAdd(validation.Name, index))
            {
                report(
                    DiagnosticCodes.DuplicateContractName,
                    $"The validation '{validation.Name}' repeats the validation at index {firstIndexByName[validation.Name].ToString(CultureInfo.InvariantCulture)}. Validation names are unique, ignoring letter case.",
                    $"{path}/name");
            }

            if (ApplicationCompiler.FindField(entity, validation.Field) is null)
            {
                report(
                    DiagnosticCodes.UnknownValidationField,
                    $"The field '{validation.Field}' of the validation '{validation.Name}' is not a field of the entity '{entity.Name}'.",
                    $"{path}/field");
            }

            if (!validation.Message.TextKey.StartsWith($"{moduleId}.", StringComparison.Ordinal))
            {
                report(
                    DiagnosticCodes.ModuleTextKeyWithoutPrefix,
                    $"The text key '{validation.Message.TextKey}' lacks the prefix '{moduleId}.'. Start each text key of the module with it.",
                    $"{path}/message/textKey");
            }

            scope ??= ExpressionScopes.ForEntity(
                entity.Fields,
                rules: rulesFor(contract),
                findEntity: findEntity,
                modules: modulesFor(contract),
                undeclaredModuleIsUnknown: true);
            var parsed = ExpressionParser.Parse(validation.Expression);
            var problem = parsed.Succeeded ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean, contract: true).Diagnostic : parsed.Diagnostic;
            if (problem is not null)
            {
                report(problem.Code, problem.Message, $"{path}/expression");
            }
        }
    }
}
