namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>sequence</c> resource: a named counter that numbers a text field. Its format holds exactly
/// one number token and may hold the year, which the schema checks. The fields that name it are
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record SequenceResource : Resource
{
    /// <summary>The format of each number, such as <c>PR-{yyyy}-{n:5}</c>.</summary>
    public required string Format { get; init; }
}
