using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>Collects the edges of one compile, in the order they are added.</summary>
internal sealed class ReferenceIndexBuilder(IReadOnlySet<string> textKeys)
{
    private readonly List<ReferenceEdge> _edges = [];

    /// <summary>The text keys that some locale has.</summary>
    public IReadOnlySet<string> TextKeys { get; } = textKeys;

    public void Add(Resource from, string path, string relation, ReferenceNode to) =>
        _edges.Add(new ReferenceEdge(ResourceNode.Of(from), path, relation, to));

    public ReferenceIndex Build() => new(_edges);
}
