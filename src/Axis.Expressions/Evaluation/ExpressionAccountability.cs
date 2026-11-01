namespace Axis.Expressions.Evaluation;

/// <summary>
/// Who answers for a write and the task whose completion it follows, which <c>accountableUser()</c>
/// and <c>decisionTask()</c> give. Both are <c>null</c> when the process instance has none yet.
/// </summary>
public sealed record ExpressionAccountability(string? AccountableUser, string? DecisionTask);
