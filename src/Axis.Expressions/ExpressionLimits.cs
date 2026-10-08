namespace Axis.Expressions;

/// <summary>The compile-time cost bounds of an expression.</summary>
public static class ExpressionLimits
{
    /// <summary>The most characters an expression may have.</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// The highest the syntax tree may be. A name or literal has depth 1, and each node and each
    /// pair of parentheses adds one level.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>The most syntax nodes an expression may have, counting each <c>in</c> item.</summary>
    public const int MaxNodes = 500;
}
