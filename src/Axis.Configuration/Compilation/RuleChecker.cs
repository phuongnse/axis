using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions;
using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the named rules of an application and builds the rules that validations and data source
/// filters call. A rule name may not be a built-in function name, its parameter names are unique
/// ignoring letter case, and its expression parses and type-checks to its result type over its
/// parameters alone. Rules that call each other in a cycle are reported once per cycle. A rule call
/// chain deeper than the limit is reported once, at the lowest rule past the limit. A rule with a
/// problem in its own expression, or in a cycle, is kept by its signature only, so that calls to it
/// are still checked without further diagnostics.
/// </summary>
internal static class RuleChecker
{
    /// <summary>Checks <paramref name="rules"/>, which come in path order, and returns the rules by name, ignoring letter case.</summary>
    public static IReadOnlyDictionary<string, ExpressionRule> Check(IReadOnlyList<RuleResource> rules, List<Diagnostic> diagnostics)
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
                ExpressionScopes.TypeOf(rule.ResultType));

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

        new Walk(resources, built, bodies, diagnostics).Run();
        return built;
    }

    /// <summary>
    /// A depth-first walk over the rule calls, in path order. A rule's body is checked once every
    /// rule it calls is done, so the rules it calls already carry their checked bodies.
    /// </summary>
    private sealed class Walk(
        Dictionary<string, RuleResource> resources,
        Dictionary<string, ExpressionRule> built,
        Dictionary<string, ExpressionNode> bodies,
        List<Diagnostic> diagnostics)
    {
        private readonly Dictionary<string, bool> _doneByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _stack = [];
        private readonly HashSet<string> _inCycle = new(StringComparer.OrdinalIgnoreCase);

        // A rule is missing from the depths when it is in a cycle, calls into a cycle or is too deep,
        // so the rules that call it are not measured and get no further diagnostic.
        private readonly Dictionary<string, int> _depthByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _deepestCalleeByName = new(StringComparer.OrdinalIgnoreCase);

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
            MeasureDepth(name, callees);
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
        /// </summary>
        private void MeasureDepth(string name, List<string> callees)
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

            if (depth > ExpressionLimits.MaxRuleCallDepth)
            {
                ReportTooDeep(name, deepest!, depth);
                return;
            }

            _depthByName[name] = depth;
            if (deepest is not null)
            {
                _deepestCalleeByName[name] = deepest;
            }
        }

        /// <summary>Reports the chain from <paramref name="name"/> down its deepest callees, at the expression of <paramref name="name"/>.</summary>
        private void ReportTooDeep(string name, string deepest, int depth)
        {
            var chain = new List<string> { name };
            for (string? next = deepest; next is not null; next = _deepestCalleeByName.GetValueOrDefault(next))
            {
                chain.Add(next);
            }

            var rule = resources[name];
            var names = string.Join(" → ", chain.Select(member => $"'{resources[member].Name}'"));
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.RuleCallTooDeep,
                $"Rules {names} nest calls {depth} deep, past the limit of {ExpressionLimits.MaxRuleCallDepth}.",
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

            var check = ExpressionTypeChecker.Check(body, new ExpressionScope(parameters, built.Values), rule.ResultType);
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
        /// a rule call.
        /// </summary>
        private List<string> Callees(ExpressionNode body)
        {
            var callees = new List<string>();
            Collect(body);
            return [.. callees.Distinct(StringComparer.OrdinalIgnoreCase)];

            void Collect(ExpressionNode node)
            {
                switch (node)
                {
                    case CallNode call:
                        if (!ExpressionFunctions.IsFunction(call.Name)
                            && resources.ContainsKey(call.Name))
                        {
                            callees.Add(call.Name);
                        }

                        foreach (var argument in call.Arguments)
                        {
                            Collect(argument);
                        }

                        break;
                    case MemberNode member:
                        Collect(member.Target);
                        break;
                    case UnaryNode unary:
                        Collect(unary.Operand);
                        break;
                    case BinaryNode binary:
                        Collect(binary.Left);
                        Collect(binary.Right);
                        break;
                    case IsNullNode isNull:
                        Collect(isNull.Operand);
                        break;
                    case InNode inNode:
                        Collect(inNode.Operand);
                        foreach (var item in inNode.Items)
                        {
                            Collect(item);
                        }

                        break;
                }
            }
        }
    }
}
