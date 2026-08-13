using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;

namespace Axis.BusinessObjects.Application;

public static class BusinessObjectProductActions
{
    public const string DefinitionResourceType = "business-object.definition";
    public const string RecordResourceType = "business-object.record";
    public const string DefinitionReadPublished = "business-object.definition.read-published";
    public const string RecordCreate = "business-object.record.create";
    public const string RecordList = "business-object.record.list";
    public const string RecordRead = "business-object.record.read";
    public const string RecordSave = "business-object.record.save";
    public const string RecordSubmit = "business-object.record.submit";

    public static IReadOnlyList<ProductActionDescriptor> Descriptors { get; } = Array.AsReadOnly<ProductActionDescriptor>(
    [
        new(DefinitionReadPublished, DefinitionResourceType, ProductActionKind.NonRecord),
        new(RecordCreate, RecordResourceType, ProductActionKind.Record),
        new(RecordList, RecordResourceType, ProductActionKind.Record),
        new(RecordRead, RecordResourceType, ProductActionKind.Record),
        new(RecordSave, RecordResourceType, ProductActionKind.Record),
        new(RecordSubmit, RecordResourceType, ProductActionKind.Record),
    ]);
}

public static class BusinessObjectAuthorization
{
    public static async Task<WorkspaceProductBuilderDecision> AuthorizeBuilderAsync(
        IWorkspaceProductBuilderAuthorization authorization,
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken)
    {
        if (subject.Id == Guid.Empty || subject.Kind != SubjectKind.Human)
            return WorkspaceProductBuilderDecision.Denied;

        try
        {
            return await authorization.AuthorizeAsync(workspaceId, subject, cancellationToken);
        }
        catch
        {
            return WorkspaceProductBuilderDecision.Unavailable;
        }
    }

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
        try
        {
            return await authorization.AuthorizeAsync(
                new ProductAuthorizationRequest(
                    workspaceId,
                    subject,
                    actionKey,
                    resourceType,
                    resourceKey,
                    string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim()),
                cancellationToken);
        }
        catch
        {
            return ProductAuthorizationDecision.Unavailable;
        }
    }
}
