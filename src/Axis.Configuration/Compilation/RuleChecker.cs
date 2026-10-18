using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions;
using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the named rules of an application and builds the rules that validations, data source
/// filters and process conditions call. A rule name may not be a built-in function name, its parameter names are unique
/// ignoring letter case, and its expression parses and type-checks to its result type over its
/// parameters alone. Rules that call each other in a cycle are reported once per cycle. A rule call
/// chain deeper than the limit is reported once, at the lowest rule past the limit. A rule with a
/// problem in its own expression, or in a cycle, is kept by its signature only, so that calls to it
/// are still checked without further diagnostics. A body may call a module rule as
/// <c>module:Name(…)</c> when it is given the module rules and the declared module ids. A chain that
/// passes into module rules counts every rule in it toward the depth limit.
/// </summary>
internal static class RuleChecker
{
    /// <summary>
    /// Checks <paramref name="rules"/>, which come in path order, and returns the rules by name, ignoring letter case.
    /// <paramref name="moduleRules"/> are the checked rules of the declared modules and
    /// <paramref name="modules"/> the declared module ids. Both are null for a module's own rules,
    /// which call no module rule.
    /// </summary>
    public static IReadOnlyDictionary<string, ExpressionRule> Check(
        IReadOnlyList<RuleResource> rules,
        List<Diagnostic> diagnostics,
        IReadOnlyCollection<ExpressionRule>? moduleRules = null,
        IReadOnlySet<string>? modules = null)
    {
        // Rule names are unique ignoring letter case; a second rule with the same name is already
        // reported by the loader, so calls resolve to the first one.
        var resources = new Dictionary<string, RuleResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            resources.TryAdd(rule.Name, rule);
        }

        var built = new Dictionary<string, ExpressionRule>(StringComparer.OrdinalIgnoreCase);
        var bodies = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in resources.Values)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, rule.File, path, rule.Id));

            if (ExpressionFunctions.IsFunction(rule.Name))
            {
                Report(
                    DiagnosticCodes.RuleNameIsFunction,
                    $"The rule name '{rule.Name}' is the name of a built-in function. Choose another name.",
                    "/name");
            }

            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < rule.Parameters.Count; index++)
            {
                var name = rule.Parameters[index].Name;
                if (!firstIndexByName.TryAdd(name, index))
                {
                    var firstIndex = firstIndexByName[name];
                    Report(
                        DiagnosticCodes.DuplicateRuleParameter,
                        $"The parameter name '{name}' is already used by parameter '{rule.Parameters[firstIndex].Name}' at '/parameters/{firstIndex}'.",
                        $"/parameters/{index}/name");
                }
            }

            built[rule.Name] = new ExpressionRule(
                rule.Name,
                [.. rule.Parameters.Select(parameter => new ExpressionRuleParameter(parameter.Name, ExpressionScopes.TypeOf(parameter.Type)))],
                ExpressionScopes.TypeOf(rule.ResultType))
            {
                Expression = rule.Expression,
            };

            var parsed = ExpressionParser.Parse(rule.Expression);
            if (parsed.Succeeded)
            {
                bodies[rule.Name] = parsed.Expression;
            }
            else
            {
                Report(parsed.Diagnostic.Code, parsed.Diagnostic.Message, "/expression");
            }
        }

        new Walk(resources, built, bodies, diagnostics, moduleRules ?? [], modules).Run();
        return built;
    }

    /// <summary>
    /// Checks each module's rules on their own, so a module rule calls only rules of its module, and
    /// returns them by <c>&lt;module id&gt;/&lt;name&gt;</c>, ignoring letter case.
    /// </summary>
    public static IReadOnlyDictionary<string, ExpressionRule> CheckModules(
        IReadOnlyList<(string ModuleId, IReadOnlyList<RuleResource> Rules)> modules, List<Diagnostic> diagnostics)
    {
        var rules = new Dictionary<string, ExpressionRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (moduleId, moduleRules) in modules)
        {
            foreach (var (name, rule) in Check(moduleRules, diagnostics))
            {
                rules[$"{moduleId}/{name}"] = rule with { Module = moduleId };
            }
        }

        return rules;
    }

    /// <summary>
    /// A depth-first walk over the rule calls, in path order. A rule's body is checked once every
    /// rule it calls is done, so the rules it calls already carry their checked bodies.
    /// </summary>
    private sealed class Walk(
        Dictionary<string, RuleResource> resources,
        Dictionary<string, ExpressionRule> built,
        Dictionary<string, ExpressionNode> bodies,
        List<Diagnostic> diagnostics,
        IReadOnlyCollection<ExpressionRule> moduleRules,
        IReadOnlySet<string>? modules)
    {
        private readonly ExpressionScope _moduleScope = new([], moduleRules, modules: modules);
        private readonly Dictionary<string, bool> _doneByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _stack = [];
        private readonly HashSet<string> _inCycle = new(StringComparer.OrdinalIgnoreCase);

        // A rule is missing from the depths when it is in a cycle, calls into a cycle or is too deep,
        // so the rules that call it are not measured and get no further diagnostic.
        private readonly Dictionary<string, int> _depthByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _deepestCalleeByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExpressionRule> _deepestModuleRuleByName = new(StringComparer.OrdinalIgnoreCase);

        public void Run()
        {
            foreach (var name in resources.Keys)
            {
                Visit(name);
            }
        }

        private void Visit(string name)
        {
            if (_doneByName.ContainsKey(name))
            {
                return;
            }

            _doneByName[name] = false;
            _stack.Add(name);
            var callees = bodies.TryGetValue(name, out var body) ? Callees(body) : [];
            foreach (var callee in callees)
            {
                if (_doneByName.TryGetValue(callee, out var done) && !done)
                {
                    ReportCycle(callee);
                }
                else
                {
                    Visit(callee);
                }
            }

            _stack.RemoveAt(_stack.Count - 1);
            _doneByName[name] = true;
            MeasureDepth(name, callees, body is null ? [] : ModuleCallees(body));
            CheckBody(name, body);
        }

        /// <summary>Reports the cycle from <paramref name="start"/>, which is on the stack, back to it, at the expression of <paramref name="start"/>.</summary>
        private void ReportCycle(string start)
        {
            var members = _stack.Skip(_stack.FindIndex(name => string.Equals(name, start, StringComparison.OrdinalIgnoreCase))).ToList();
            _inCycle.UnionWith(members);
            var rule = resources[start];
            var names = string.Join(" → ", members.Append(start).Select(name => $"'{resources[name].Name}'"));
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.RuleCallCycle,
                $"Rules {names} call each other in a cycle.",
                rule.File,
                "/expression",
                rule.Id));
        }

        /// <summary>
        /// Keeps the call depth of a rule whose callees are all measured: the rule itself plus its
        /// deepest callee. Every callee is done, or is on the stack and then the rule is in a cycle.
        /// A module rule counts with the rules it calls in turn.
        /// </summary>
        private void MeasureDepth(string name, List<string> callees, List<ExpressionRule> moduleCallees)
        {
            if (_inCycle.Contains(name))
            {
                return;
            }

            var depth = 1;
            string? deepest = null;
            foreach (var callee in callees)
            {
                if (!_depthByName.TryGetValue(callee, out var calleeDepth))
                {
                    return;
                }

                if (calleeDepth + 1 > depth)
                {
                    depth = calleeDepth + 1;
                    deepest = callee;
                }
            }

            ExpressionRule? deepestModuleRule = null;
            foreach (var moduleRule in moduleCallees)
            {
                if (Depth(moduleRule) is not { } moduleDepth)
                {
                    return;
                }

                if (moduleDepth + 1 > depth)
                {
                    depth = moduleDepth + 1;
                    deepest = null;
                    deepestModuleRule = moduleRule;
                }
            }

            if (depth > ExpressionLimits.MaxRuleCallDepth)
            {
                ReportTooDeep(name, deepest, deepestModuleRule, depth);
                return;
            }

            _depthByName[name] = depth;
            if (deepest is not null)
            {
                _deepestCalleeByName[name] = deepest;
            }
            else if (deepestModuleRule is not null)
            {
                _deepestModuleRuleByName[name] = deepestModuleRule;
            }
        }

        /// <summary>
        /// The call depth of a checked module rule: itself plus its deepest callee. Null when the rule
        /// or a rule it calls has no checked body.
        /// </summary>
        private static int? Depth(ExpressionRule rule)
        {
            if (rule.BodyCheck is null)
            {
                return null;
            }

            var deepest = 0;
            foreach (var callee in rule.BodyCheck.RuleCalls.Values)
            {
                if (Depth(callee) is not { } calleeDepth)
                {
                    return null;
                }

                deepest = Math.Max(deepest, calleeDepth);
            }

            return deepest + 1;
        }

        /// <summary>Reports the chain from <paramref name="name"/> down its deepest callees, at the expression of <paramref name="name"/>.</summary>
        private void ReportTooDeep(string name, string? deepest, ExpressionRule? deepestModuleRule, int depth)
        {
            var chain = new List<string> { name };
            var last = name;
            for (string? next = deepest; next is not null; next = _deepestCalleeByName.GetValueOrDefault(next))
            {
                chain.Add(next);
                last = next;
            }

            var names = chain.Select(member => $"'{resources[member].Name}'").ToList();
            if (deepestModuleRule is null && _deepestModuleRuleByName.TryGetValue(last, out var tail))
            {
                deepestModuleRule = tail;
            }

            if (deepestModuleRule is not null)
            {
                names.Add($"'{deepestModuleRule.Module}:{deepestModuleRule.Name}'");
            }

            var rule = resources[name];
            var chainText = string.Join(" → ", names);
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.RuleCallTooDeep,
                $"Rules {chainText} nest calls {depth} deep, past the limit of {ExpressionLimits.MaxRuleCallDepth}.",
                rule.File,
                "/expression",
                rule.Id));
        }

        /// <summary>
        /// Checks a parsed body over the rule's parameters and every rule, and keeps it unless the
        /// rule is in a cycle. A repeated parameter name keeps its first type.
        /// </summary>
        private void CheckBody(string name, ExpressionNode? body)
        {
            if (body is null)
            {
                return;
            }

            var resource = resources[name];
            var rule = built[name];
            var parameters = new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in rule.Parameters)
            {
                parameters.TryAdd(parameter.Name, parameter.Type);
            }

            var check = ExpressionTypeChecker.Check(body, new ExpressionScope(parameters, built.Values.Concat(moduleRules), modules: modules), rule.ResultType);
            if (!check.Succeeded)
            {
                diagnostics.Add(new Diagnostic(check.Diagnostic.Code, check.Diagnostic.Message, resource.File, "/expression", resource.Id));
            }
            else if (!_inCycle.Contains(name))
            {
                built[name] = rule with { Body = body, BodyCheck = check };
            }
        }

        /// <summary>
        /// The known rules a body calls, each once, in tree order. A built-in function name is never
        /// a rule call, and a qualified call never names an application rule.
        /// </summary>
        private List<string> Callees(ExpressionNode body)
        {
            var callees = new List<string>();
            ForEachCall(body, call =>
            {
                if (call.Module is null && !ExpressionFunctions.IsFunction(call.Name) && resources.ContainsKey(call.Name))
                {
                    callees.Add(call.Name);
                }
            });
            return [.. callees.Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>The module rules a body calls with a qualified call, each once, in tree order.</summary>
        private List<ExpressionRule> ModuleCallees(ExpressionNode body)
        {
            var callees = new List<ExpressionRule>();
            ForEachCall(body, call =>
            {
                if (call.Module is { } module
                    && _moduleScope.TryGetModuleRule(module, call.Name, out var rule)
                    && !callees.Contains(rule))
                {
                    callees.Add(rule);
                }
            });
            return callees;
        }

        private static void ForEachCall(ExpressionNode node, Action<CallNode> visit)
        {
            switch (node)
            {
                case CallNode call:
                    visit(call);
                    foreach (var argument in call.Arguments)
                    {
                        ForEachCall(argument, visit);
                    }

                    break;
                case MemberNode member:
                    ForEachCall(member.Target, visit);
                    break;
                case UnaryNode unary:
                    ForEachCall(unary.Operand, visit);
                    break;
                case BinaryNode binary:
                    ForEachCall(binary.Left, visit);
                    ForEachCall(binary.Right, visit);
                    break;
                case IsNullNode isNull:
                    ForEachCall(isNull.Operand, visit);
                    break;
                case InNode inNode:
                    ForEachCall(inNode.Operand, visit);
                    foreach (var item in inNode.Items)
                    {
                        ForEachCall(item, visit);
                    }

                    break;
            }
        }
    }
}
