namespace Axis.Configuration.Resources;

/// <summary>
/// The connector operation a <c>callConnector</c> step calls, as written: the connector's name and
/// the name of one of its operations. Whether both exist is checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ConnectorStepReference
{
    public required string Name { get; init; }

    public required string Operation { get; init; }
}
