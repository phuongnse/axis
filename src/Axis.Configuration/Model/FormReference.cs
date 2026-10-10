namespace Axis.Configuration.Model;

/// <summary>A form that a widget lays out its fields with, by id and name rather than by model object.</summary>
public sealed record FormReference(Guid Id, string Name);
