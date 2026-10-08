namespace Axis.Configuration.Resources;

/// <summary>
/// A projected field as written in the data source file: the <c>name</c> that is its key in a row
/// and the <c>path</c> to a value of the root entity.
/// </summary>
public sealed record DataSourceFieldDefinition
{
    public required string Name { get; init; }

    public required string Path { get; init; }
}
