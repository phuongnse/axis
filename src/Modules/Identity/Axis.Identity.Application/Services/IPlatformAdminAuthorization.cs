namespace Axis.Identity.Application.Services;

/// <summary>US-012: platform operators who may change org plans outside tenant RBAC.</summary>
public interface IPlatformAdminAuthorization
{
    bool IsPlatformAdmin(Guid userId);
}
