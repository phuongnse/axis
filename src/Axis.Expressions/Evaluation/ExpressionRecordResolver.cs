namespace Axis.Expressions.Evaluation;

/// <summary>
/// Finds the field values of the record <paramref name="id"/> of the entity
/// <paramref name="entity"/>, or null when there is none. Entity names match ignoring letter case.
/// The interpreter calls it to follow a path through a reference field.
/// </summary>
public delegate ExpressionValues? ExpressionRecordResolver(string entity, Guid id);
