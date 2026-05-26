using Axis.Identity.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class ConfigPlatformAdminAuthorization(IConfiguration configuration) : IPlatformAdminAuthorization
{
    public bool IsPlatformAdmin(Guid userId)
    {
        string[] configured = configuration.GetSection("PlatformAdmin:UserIds").Get<string[]>() ?? [];
        return configured.Any(id => Guid.TryParse(id, out Guid parsed) && parsed == userId);
    }
}
