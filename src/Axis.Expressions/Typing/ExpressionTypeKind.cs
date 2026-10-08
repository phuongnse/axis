using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>The kind of an expression's type. <see cref="Null"/> is the type of the literal <c>null</c>.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The names are the expression types of the language.")]
public enum ExpressionTypeKind
{
    Null,
    Text,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Enum,
    Reference,
}
