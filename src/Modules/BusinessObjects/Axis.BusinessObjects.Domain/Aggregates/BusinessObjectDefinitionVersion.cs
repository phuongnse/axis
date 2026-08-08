using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersion : Entity<BusinessObjectDefinitionVersionId>
{
    private readonly List<BusinessObjectDefinitionVersionField> _fields = [];

    private BusinessObjectDefinitionVersion()
        : base(default)
    {
        Name = string.Empty;
        Key = BusinessObjectDefinitionKey.Create("definition").Value;
    }

    public BusinessObjectDefinitionId SourceDefinitionId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; }
    public BusinessObjectDefinitionKey Key { get; private set; }
    public SubjectReference PublishedBySubject { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public IReadOnlyList<BusinessObjectDefinitionVersionField> Fields => _fields.AsReadOnly();

    private BusinessObjectDefinitionVersion(
        BusinessObjectDefinitionVersionId id,
        BusinessObjectDefinitionId sourceDefinitionId,
        Guid workspaceId,
        int versionNumber,
        string name,
        BusinessObjectDefinitionKey key,
        SubjectReference publishedBySubject,
        DateTime publishedAt)
        : base(id)
    {
        SourceDefinitionId = sourceDefinitionId;
        WorkspaceId = workspaceId;
        VersionNumber = versionNumber;
        Name = name;
        Key = key;
        PublishedBySubject = publishedBySubject;
        PublishedAt = publishedAt;
    }

    public static BusinessObjectDefinitionVersion Create(
        BusinessObjectDefinition definition,
        int versionNumber,
        SubjectReference publishedBySubject,
        DateTime publishedAt)
    {
        BusinessObjectDefinitionVersion version = new(
            BusinessObjectDefinitionVersionId.New(),
            definition.Id,
            definition.WorkspaceId,
            versionNumber,
            definition.Name,
            definition.Key,
            publishedBySubject,
            publishedAt);
        version._fields.AddRange(definition.Fields
            .OrderBy(field => field.Order)
            .Select(BusinessObjectDefinitionVersionField.FromCurrentDefinition));
        return version;
    }
}
