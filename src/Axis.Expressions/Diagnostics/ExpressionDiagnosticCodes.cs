namespace Axis.Expressions.Diagnostics;

/// <summary>
/// Stable diagnostic codes for expressions. They share the <c>AXCnnnn</c> range and the docs table
/// with the configuration codes. A code never changes meaning once published.
/// </summary>
public static class ExpressionDiagnosticCodes
{
    public const string SyntaxError = "AXC0035";
    public const string TooLong = "AXC0036";
    public const string TooDeep = "AXC0037";
    public const string TooManyNodes = "AXC0038";
    public const string UnknownName = "AXC0046";
    public const string TypeMismatch = "AXC0047";
    public const string ResultTypeMismatch = "AXC0048";
    public const string UnknownEnumValue = "AXC0049";
    public const string UnknownFunction = "AXC0050";
    public const string WrongArgumentCount = "AXC0051";
    public const string OutsideSqlSubset = "AXC0053";
    public const string TooManyHops = "AXC0058";
}
