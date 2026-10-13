namespace Axis.Configuration.Model;

/// <summary>An event that a trigger listens to, by id and name rather than by model object.</summary>
public sealed record EventReference(Guid Id, string Name);
