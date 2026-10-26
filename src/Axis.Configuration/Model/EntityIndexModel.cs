namespace Axis.Configuration.Model;

/// <summary>A compiled index an author declares on an entity. <see cref="Fields"/> are the declared field names, in order.</summary>
public sealed record EntityIndexModel(IReadOnlyList<string> Fields, bool Unique);
