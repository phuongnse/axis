namespace Axis.Api.Authorization;

internal sealed class ServiceProductEndpointMetadata
{
    public static ServiceProductEndpointMetadata Instance { get; } = new();

    private ServiceProductEndpointMetadata()
    {
    }
}
