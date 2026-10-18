namespace Axis.Configuration.Compilation;

/// <summary>An edge of the reference index: the resource uses <paramref name="To"/> at <paramref name="Path"/> under <paramref name="Relation"/>.</summary>
public sealed record ReferenceEdge(ResourceNode From, string Path, string Relation, ReferenceNode To);
