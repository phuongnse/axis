using Axis.Authorization.Contracts;

namespace Axis.Authorization.Application;

public sealed class ProductActionDescriptorRegistry(
    IEnumerable<ProductActionDescriptor> descriptors) : IProductActionDescriptorRegistry
{
    private readonly IReadOnlyDictionary<(string ActionKey, string ResourceType), ProductActionDescriptor>
        _descriptors = descriptors.ToDictionary(
            value => (value.ActionKey, value.ResourceType),
            value => value);

    public ProductActionDescriptor? Find(string actionKey, string resourceType) =>
        _descriptors.GetValueOrDefault((actionKey, resourceType));
}
