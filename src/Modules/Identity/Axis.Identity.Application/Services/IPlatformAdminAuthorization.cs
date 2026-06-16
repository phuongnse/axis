namespace Axis.Identity.Application.Services;

/// <summary>platform operators who may change Workspace plans outside workspace RBAC.</summary>
public interface IPlatformAdminAuthorization
{
    bool IsPlatformAdmin(Guid userId);
}
