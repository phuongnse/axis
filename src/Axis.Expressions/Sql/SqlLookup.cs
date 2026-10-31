using Axis.Expressions.Typing;

namespace Axis.Expressions.Sql;

/// <summary>
/// One exact lookup test a translated expression makes. The test compares the lookup column of
/// the field at <see cref="Path"/> with a <c>bytea[]</c> parameter that holds the hashes of
/// <see cref="Value"/>, so the caller must hash the value and bind that parameter.
/// <see cref="Name"/> is the parameter name without its <c>@</c>. <see cref="Value"/> has the CLR
/// type of its kind, as in <see cref="SqlValue"/>. When the test is against a name and not a
/// literal, <see cref="Parameter"/> holds that name as written, <see cref="Value"/> is null and
/// <see cref="Kind"/> is not set. The caller resolves the name to a data source parameter, and
/// hashes that parameter's value. A parameter without a value has no hashes.
/// </summary>
public sealed record SqlLookup(string Name, IReadOnlyList<string> Path, ExpressionTypeKind Kind, object? Value)
{
    /// <summary>The name of the data source parameter the field is tested against, or null for a literal.</summary>
    public string? Parameter { get; init; }
}
