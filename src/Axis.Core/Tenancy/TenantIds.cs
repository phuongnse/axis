namespace Axis.Core.Tenancy;

/// <summary>The rule of a tenant id that the platform and the control plane share.</summary>
public static class TenantIds
{
    /// <summary>Whether the id is one or more lowercase letters, digits or hyphens.</summary>
    public static bool IsValid(string tenantId) =>
        tenantId.Length > 0 && tenantId.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
