using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>layer</c> resource: a customization layer that names one resource of a module the application
/// declares and holds the changes to make to it. It has no <c>name</c>, so it does not derive from
/// <see cref="Resource"/>. The schema checks its shape. Where the file sits, whether its
/// <see cref="Target"/> names a resource that may be changed and whether <see cref="Changes"/> leaves
/// the identity of the target alone are checked by <see cref="Compilation.ApplicationCompiler"/>.
/// Compilation merges the changes into the module resource before any check, so the application runs the module as changed.
/// </summary>
public sealed record LayerResource
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required int FormatVersion { get; init; }

    /// <summary>The resource to change, as <c>&lt;module id&gt;/&lt;resource name&gt;</c>.</summary>
    public required string Target { get; init; }

    /// <summary>The changes to the target, as written in the file.</summary>
    public required JsonElement Changes { get; init; }

    /// <summary>The path of the file the layer was loaded from, relative to the application folder.</summary>
    [JsonIgnore]
    public string File { get; init; } = "";
}
