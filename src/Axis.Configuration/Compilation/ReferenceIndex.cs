namespace Axis.Configuration.Compilation;

/// <summary>
/// The edges between resources and the nodes they reference. <see cref="Uses"/> lists the edges
/// of a resource, and <see cref="UsedBy"/> lists the edges that reach a node.
/// </summary>
public sealed class ReferenceIndex
{
    public static ReferenceIndex Empty { get; } = new([]);

    private readonly ILookup<ReferenceNode, ReferenceEdge> _usesByNode;
    private readonly ILookup<ReferenceNode, ReferenceEdge> _usedByNode;

    internal ReferenceIndex(IReadOnlyList<ReferenceEdge> edges)
    {
        Edges = edges;
        _usesByNode = edges.ToLookup(edge => (ReferenceNode)edge.From);
        _usedByNode = edges.ToLookup(edge => edge.To);
    }

    /// <summary>Every edge, in the order it was added.</summary>
    public IReadOnlyList<ReferenceEdge> Edges { get; }

    public IReadOnlyList<ReferenceEdge> Uses(ReferenceNode node) => [.. _usesByNode[node]];

    public IReadOnlyList<ReferenceEdge> UsedBy(ReferenceNode node) => [.. _usedByNode[node]];
}
