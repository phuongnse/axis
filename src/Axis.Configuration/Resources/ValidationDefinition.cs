namespace Axis.Configuration.Resources;

/// <summary>
/// A validation rule as written in the entity file. Whether the expression is a valid boolean
/// expression over the entity's fields, and whether <see cref="Field"/> names one of them, is
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ValidationDefinition
{
    public required string Expression { get; init; }

    /// <summary>The text whose key is the message of a failure.</summary>
    public required TextReference Message { get; init; }

    /// <summary>The field a failure is reported on.</summary>
    public required string Field { get; init; }
}
