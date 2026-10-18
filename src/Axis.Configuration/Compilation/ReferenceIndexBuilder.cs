using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>Collects the edges of one compile, in the order they are added. An edge equal to one already added is kept once.</summary>
internal sealed class ReferenceIndexBuilder
{
    private readonly List<ReferenceEdge> _edges = [];
    private readonly HashSet<ReferenceEdge> _added = [];
    private readonly Dictionary<string, RuleResource> _rulesByName;

    public ReferenceIndexBuilder(IReadOnlySet<string> textKeys, IReadOnlyList<RuleResource> rules)
    {
        TextKeys = textKeys;

        // Rule names are unique ignoring letter case; a second rule with the same name is already
        // reported by the loader, so calls resolve to the first one.
        _rulesByName = new Dictionary<string, RuleResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            _rulesByName.TryAdd(rule.Name, rule);
        }
    }

    /// <summary>The text keys that some locale has.</summary>
    public IReadOnlySet<string> TextKeys { get; }

    public void Add(Resource from, string path, string relation, ReferenceNode to)
    {
        var edge = new ReferenceEdge(ResourceNode.Of(from), path, relation, to);
        if (_added.Add(edge))
        {
            _edges.Add(edge);
        }
    }

    /// <summary>Adds one rule-call edge for each rule the checked expression calls directly.</summary>
    public void AddRuleCalls(Resource from, string path, ExpressionCheckResult check)
    {
        foreach (var rule in check.RuleCalls.Values)
        {
            Add(from, path, ReferenceRelations.RuleCall, ResourceNode.Of(_rulesByName[rule.Name]));
        }
    }

    public ReferenceIndex Build() => new(_edges);
}
