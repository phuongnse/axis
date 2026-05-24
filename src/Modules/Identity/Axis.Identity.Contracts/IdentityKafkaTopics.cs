namespace Axis.Identity.Contracts;

/// <summary>Kafka topic names for Identity cross-module events (ADR-019, ADR-025).</summary>
public static class IdentityKafkaTopics
{
    public const string OrganizationVerified = "axis.identity.organization-verified";
    public const string UserDeactivated = "axis.identity.user-deactivated";
    public const string UserReactivated = "axis.identity.user-reactivated";
    public const string RoleAssigned = "axis.identity.role-assigned";
    public const string RoleRemoved = "axis.identity.role-removed";
}
