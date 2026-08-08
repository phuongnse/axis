using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;

namespace Axis.Rules.Application;

public static class RuleProductActions
{
    public const string DefinitionResourceType = "rule.definition";
    public const string BindingResourceType = "rule.binding";
    public const string DefinitionRead = "rule.definition.read";
    public const string DefinitionManage = "rule.definition.manage";
    public const string BindingRead = "rule.binding.read";
    public const string BindingManage = "rule.binding.manage";

    public static IReadOnlyList<ProductActionDescriptor> Descriptors { get; } = Array.AsReadOnly<ProductActionDescriptor>(
    [
        new(DefinitionRead, DefinitionResourceType, ProductActionKind.NonRecord),
        new(DefinitionManage, DefinitionResourceType, ProductActionKind.NonRecord),
        new(BindingRead, BindingResourceType, ProductActionKind.NonRecord),
        new(BindingManage, BindingResourceType, ProductActionKind.NonRecord),
    ]);
}

public static class RuleAuthorization
{
    public static async Task<ProductAuthorizationDecision> AuthorizeAsync(
        IProductAuthorizationService authorization,
        Guid workspaceId,
        SubjectReference subject,
        string actionKey,
        string resourceType,
        string? resourceKey,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (subject.Id == Guid.Empty || !Enum.IsDefined(subject.Kind))
            return ProductAuthorizationDecision.Denied;

        try
        {
            return await authorization.AuthorizeAsync(
                new ProductAuthorizationRequest(
                    workspaceId,
                    subject,
                    actionKey,
                    resourceType,
                    resourceKey,
                    NormalizeCorrelationId(correlationId)),
                cancellationToken);
        }
        catch
        {
            return ProductAuthorizationDecision.Unavailable;
        }
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        string normalized = correlationId?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? Guid.NewGuid().ToString("N")
            : normalized[..Math.Min(normalized.Length, 120)];
    }
}
