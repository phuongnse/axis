namespace Axis.Identity.Application.Services;

/// <summary>platform operators who may change Tenant plans outside tenant RBAC.</summary>
public interface IPlatformAdminAuthorization
{
    bool IsPlatformAdmin(Guid userId);
}
