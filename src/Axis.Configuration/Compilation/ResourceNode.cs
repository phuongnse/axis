using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>A resource as a node of the reference index.</summary>
public sealed record ResourceNode(Guid Id, string Kind, string Name, string File) : ReferenceNode
{
    public static ResourceNode Of(Resource resource) => new(resource.Id, resource.Kind, resource.Name, resource.File);
}
