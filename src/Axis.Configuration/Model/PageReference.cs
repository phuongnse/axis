namespace Axis.Configuration.Model;

/// <summary>A page that a widget or navigation entry points to, by id and name rather than by model object.</summary>
public sealed record PageReference(Guid Id, string Name);
