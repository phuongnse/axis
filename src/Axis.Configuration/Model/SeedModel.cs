using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled seed: its records with their entity resolved. The values are not checked yet.</summary>
public sealed record SeedModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The seed's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required EntityReference Entity { get; init; }

    /// <summary>The seed records, in file order.</summary>
    public required IReadOnlyList<SeedRecordDefinition> Records { get; init; }
}
