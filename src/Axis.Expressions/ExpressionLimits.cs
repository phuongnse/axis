namespace Axis.Expressions;

/// <summary>The cost bounds of an expression.</summary>
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

    /// <summary>
    /// The most reference fields a path may go through. In <c>department.manager.name</c> each
    /// <c>.</c> is one hop, so that path takes 2.
    /// </summary>
    public const int MaxHops = 3;

    /// <summary>
    /// The most steps one evaluation may take. This is a run-time bound: each node evaluated is one
    /// step, a <c>concat</c> also pays for the text it builds (see <see cref="CharactersPerStep"/>), and
    /// evaluation stops with an error as soon as it is used up.
    /// </summary>
    public const int MaxSteps = 10_000;

    /// <summary>
    /// The characters one extra step pays for: a <c>concat</c> costs one more step for each full
    /// 1,000 characters of its result.
    /// </summary>
    public const int CharactersPerStep = 1000;
}
