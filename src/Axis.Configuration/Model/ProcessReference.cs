namespace Axis.Configuration.Model;

/// <summary>A process that a widget action starts, by id and name rather than by model object.</summary>
public sealed record ProcessReference(Guid Id, string Name);
