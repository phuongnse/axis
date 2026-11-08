namespace Axis.Expressions.Typing;

/// <summary>
/// What the type check was given at one expression place: the scope, the type the place needs and
/// the flags that decide which functions it allows. <see cref="Expected"/> is
/// <see cref="ExpressionType.Null"/> when any type will do. <see cref="Sql"/> says whether the
/// place's expression runs as SQL, so the place offers only the SQL subset.
/// </summary>
public sealed record ExpressionPlace(
    ExpressionScope Scope, ExpressionType Expected, bool StoredValue, bool CurrentUser, bool Contract, bool StepSet, bool Sql = false)
{
    /// <summary>The names of the functions and aggregates the type check allows at the place.</summary>
    public IReadOnlyList<string> Functions => ExpressionTypeChecker.AllowedFunctions(this);
}
