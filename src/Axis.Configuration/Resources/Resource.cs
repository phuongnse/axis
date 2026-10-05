using System.Text.Json.Serialization;

namespace Axis.Configuration.Resources;

/// <summary>One typed configuration item loaded from a single file.</summary>
public abstract record Resource
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required int FormatVersion { get; init; }

    public TextReference? Label { get; init; }

    /// <summary>
    /// The file the resource was loaded from, relative to the application folder with <c>/</c>
    /// separators. Set by the loader, never read from the file.
    /// </summary>
    [JsonIgnore]
    public string File { get; init; } = "";
}
