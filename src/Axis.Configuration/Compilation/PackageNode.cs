namespace Axis.Configuration.Compilation;

/// <summary>An extension package as a node of the reference index, by its id.</summary>
public sealed record PackageNode(string Id) : ReferenceNode;
