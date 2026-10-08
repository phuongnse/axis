namespace Axis.Expressions.Parsing;

/// <summary>
/// One token. <see cref="Text"/> is the name as written, or the text literal's value. Only the
/// value field that fits <see cref="Kind"/> is set.
/// </summary>
internal readonly record struct Token(
    TokenKind Kind,
    int Offset,
    int Length,
    string? Text = null,
    long Integer = 0,
    decimal Decimal = 0);
