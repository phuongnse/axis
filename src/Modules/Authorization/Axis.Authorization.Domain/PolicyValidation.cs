using System.Globalization;
using System.Text;
namespace Axis.Authorization.Domain;

public enum ProductActionScope { None = 0, Own = 1, All = 2 }
public enum ProductActionKind { NonRecord = 0, Record = 1 }
public sealed record ProductActionDescriptor(string ActionKey, string ResourceType, ProductActionKind Kind);
public sealed record ProductRolePresentation(string DisplayName, string? Description);
public sealed record ProductPolicyRole(string RoleKey, IReadOnlyDictionary<string, ProductRolePresentation> Presentation);
public sealed record ProductPolicyGrant(string RoleKey, string ActionKey, string ResourceType, string? ResourceKey, ProductActionScope Scope);
public sealed record ProductPolicyComponent(string PolicyKey, Guid VersionId, IReadOnlyList<ProductPolicyRole> Roles, IReadOnlyList<ProductPolicyGrant> Grants);

public static class ProductPolicyValidation
{
    public static string? Validate(ProductPolicyComponent component, IReadOnlyCollection<ProductActionDescriptor> descriptors)
    {
        if (component.VersionId == Guid.Empty || !Key(component.PolicyKey) || component.Roles.Count == 0)
            return "authorization.policy_invalid";
        Dictionary<string, ProductActionDescriptor> actions = new(StringComparer.Ordinal);
        foreach (ProductActionDescriptor descriptor in descriptors)
        {
            if (!Key(descriptor.ActionKey) || !Key(descriptor.ResourceType) || !Enum.IsDefined(descriptor.Kind) ||
                !actions.TryAdd(Identity(descriptor.ActionKey, descriptor.ResourceType), descriptor))
                return "authorization.descriptor_invalid";
        }

        HashSet<string> roles = new(StringComparer.Ordinal);
        foreach (ProductPolicyRole role in component.Roles)
        {
            if (!Key(role.RoleKey) || !roles.Add(role.RoleKey) || !role.Presentation.TryGetValue("en", out _))
                return "authorization.role_invalid";
            HashSet<string> languages = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string languageTag, ProductRolePresentation presentation) in role.Presentation)
                if (!languages.Add(languageTag) || !LanguageTag(languageTag) || !Text(presentation.DisplayName, 256) || (presentation.Description is not null && !Text(presentation.Description, 2048)))
                    return "authorization.role_presentation_invalid";
        }

        HashSet<string> grants = new(StringComparer.Ordinal);
        foreach (ProductPolicyGrant grant in component.Grants)
        {
            string grantIdentity = string.Join("\u001f", grant.RoleKey, grant.ActionKey, grant.ResourceType, grant.ResourceKey ?? string.Empty, grant.Scope);
            if (!Key(grant.RoleKey) || !Key(grant.ActionKey) || !Key(grant.ResourceType) || (grant.ResourceKey is not null && !Key(grant.ResourceKey)) ||
                !Enum.IsDefined(grant.Scope) || !grants.Add(grantIdentity) ||
                !roles.Contains(grant.RoleKey) || !actions.TryGetValue(Identity(grant.ActionKey, grant.ResourceType), out ProductActionDescriptor? descriptor) ||
                (descriptor.Kind == ProductActionKind.NonRecord && grant.Scope != ProductActionScope.None) ||
                (descriptor.Kind == ProductActionKind.Record && grant.Scope is not (ProductActionScope.Own or ProductActionScope.All)))
                return "authorization.grant_invalid";
        }
        return null;
    }

    private static string Identity(string actionKey, string resourceType) => actionKey + "\u001f" + resourceType;
    private static bool Key(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && value.IsNormalized(NormalizationForm.FormC);
    private static bool LanguageTag(string value)
    {
        try
        {
            string canonical = CultureInfo.GetCultureInfo(value).Name;
            return value == value.Trim()
                && canonical.Length > 0
                && StringComparer.Ordinal.Equals(value, canonical);
        }
        catch (CultureNotFoundException) { return false; }
    }
}
