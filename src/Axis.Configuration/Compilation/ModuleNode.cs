namespace Axis.Configuration.Compilation;

/// <summary>A module as a node of the reference index, by its id.</summary>
public sealed record ModuleNode(string Id) : ReferenceNode;
