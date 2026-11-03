namespace Axis.Configuration.Model;

/// <summary>
/// How a reference field finds its record. <paramref name="Field"/> is a unique field of the target
/// entity and <paramref name="From"/> a field of this entity, both in the letter case they were declared in.
/// </summary>
public sealed record FieldMatchModel(string Field, string From);
