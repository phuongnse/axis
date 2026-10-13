namespace Axis.Configuration.Resources;

/// <summary>The <c>application</c> resource at the root of an application folder.</summary>
public sealed record ApplicationManifest : Resource
{
    /// <summary>The extension packages the application uses, in the order written.</summary>
    public IReadOnlyList<ApplicationPackageDefinition> Packages { get; init; } = [];
}
