namespace Axis.Identity.Application.Services;

public enum TenantOrganizationAccessStatus
{
    Allowed,
    OrganizationNotFound,
    OrganizationSuspended,
    OrganizationNotReady,
}

public sealed record TenantOrganizationAccessResult(TenantOrganizationAccessStatus Status);
