using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Shared.Application;

namespace Axis.Authorization.Application;

public sealed record ProductRoleOptionDto(
    Guid PolicyVersionId,
    string PolicyKey,
    string RoleKey,
    string DisplayName,
    string? Description);

public sealed record ProductRoleAssignmentDto(
    Guid WorkspaceId,
    SubjectReferenceDto Subject,
    Guid PolicyVersionId,
    string RoleKey,
    bool IsActive,
    int Revision,
    [property: Required] ResourceMetadataDto Metadata)
{
    public static ProductRoleAssignmentDto From(StoredProductRoleAssignment value) =>
        new(
            value.WorkspaceId,
            SubjectReferenceDto.From(value.Subject),
            value.PolicyVersionId,
            value.RoleKey,
            value.IsActive,
            value.Revision,
            ResourceMetadataMapping.From(
                value.Revision,
                value.CreatedBy,
                value.CreatedAt,
                value.UpdatedBy,
                value.UpdatedAt));
}

public sealed record ProductRoleManagementResult(
    bool IsSuccess,
    IReadOnlyList<ProductRoleOptionDto> Roles,
    IReadOnlyList<ProductRoleAssignmentDto> Assignments,
    string? Error);

public sealed class ProductRoleManagementQueryService(
    IAuthorizationAdministratorAuthority administrators,
    IInstalledProductPolicyStore policies,
    IProductRoleAssignmentStore assignments)
{
    public async Task<ProductRoleManagementResult> GetAsync(
        Guid workspaceId,
        SubjectReference actor,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || actor.Kind != SubjectKind.Human || actor.Id == Guid.Empty)
            return new(false, [], [], "invalid");
        if (!await administrators.IsAdministratorAsync(workspaceId, actor, cancellationToken))
            return new(false, [], [], "authority_denied");

        string requestedLanguage = NormalizeLanguage(language);
        IReadOnlyList<StoredProductPolicy> installed = await policies.ListAsync(workspaceId, cancellationToken);
        ProductRoleOptionDto[] roles = installed
            .SelectMany(policy => policy.Component.Roles.Select(role =>
            {
                ProductRolePresentation presentation = role.Presentation
                    .FirstOrDefault(value => value.Key.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase)).Value
                    ?? role.Presentation["en"];
                return new ProductRoleOptionDto(
                    policy.Component.VersionId,
                    policy.Component.PolicyKey,
                    role.RoleKey,
                    presentation.DisplayName,
                    presentation.Description);
            }))
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.PolicyKey, StringComparer.Ordinal)
            .ThenBy(value => value.RoleKey, StringComparer.Ordinal)
            .ToArray();
        ProductRoleAssignmentDto[] current = (await assignments.ListAsync(workspaceId, cancellationToken))
            .Select(ProductRoleAssignmentDto.From)
            .OrderBy(value => value.Subject.Kind)
            .ThenBy(value => value.Subject.SubjectId)
            .ThenBy(value => value.RoleKey, StringComparer.Ordinal)
            .ToArray();
        return new(true, roles, current, null);
    }

    private static string NormalizeLanguage(string language)
    {
        try
        {
            string value = CultureInfo.GetCultureInfo(language.Trim()).Name;
            return string.IsNullOrEmpty(value) ? "en" : value;
        }
        catch (CultureNotFoundException)
        {
            return "en";
        }
    }
}
