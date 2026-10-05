namespace Axis.Configuration.Resources;

/// <summary>One typed configuration item loaded from a single file.</summary>
public abstract record Resource
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required int FormatVersion { get; init; }

    public TextReference? Label { get; init; }
}
