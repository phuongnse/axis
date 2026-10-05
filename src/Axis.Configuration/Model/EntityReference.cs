namespace Axis.Configuration.Model;

/// <summary>The entity a reference field points to, by id and name rather than by model object.</summary>
public sealed record EntityReference(Guid Id, string Name);
