using Axis.Solutions.Application;

namespace Axis.Api.Solutions;

internal sealed class ConfigurationAxisOpenApiDigestProvider(IConfiguration configuration)
    : ICurrentAxisOpenApiDigestProvider
{
    public string? CurrentSha256 => configuration["Solutions:AxisOpenApiSha256"];
}
