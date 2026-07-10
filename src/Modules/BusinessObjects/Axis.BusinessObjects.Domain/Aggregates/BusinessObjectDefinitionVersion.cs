using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed class BusinessObjectDefinitionVersion : Entity<BusinessObjectDefinitionVersionId>
{
    private readonly List<BusinessObjectDefinitionVersionField> _fields = [];

    public BusinessObjectDefinitionId SourceDefinitionId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; }
    public BusinessObjectDefinitionKey Key { get; private set; }
    public Guid PublishedByUserId { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public IReadOnlyList<BusinessObjectDefinitionVersionField> Fields => _fields.AsReadOnly();

    private BusinessObjectDefinitionVersion(
        BusinessObjectDefinitionVersionId id,
        BusinessObjectDefinitionId sourceDefinitionId,
        Guid workspaceId,
        int versionNumber,
        string name,
        BusinessObjectDefinitionKey key,
        Guid publishedByUserId,
        DateTime publishedAt)
        : base(id)
    {
        SourceDefinitionId = sourceDefinitionId;
        WorkspaceId = workspaceId;
        VersionNumber = versionNumber;
        Name = name;
        Key = key;
        PublishedByUserId = publishedByUserId;
        PublishedAt = publishedAt;
    }

    public static BusinessObjectDefinitionVersion Create(
        BusinessObjectDefinition definition,
        int versionNumber,
        Guid publishedByUserId,
        DateTime publishedAt)
    {
        BusinessObjectDefinitionVersion version = new(
            BusinessObjectDefinitionVersionId.New(),
            definition.Id,
            definition.WorkspaceId,
            versionNumber,
            definition.Name,
            definition.Key,
            publishedByUserId,
            publishedAt);
        version._fields.AddRange(definition.Fields
            .OrderBy(field => field.Order)
            .Select(BusinessObjectDefinitionVersionField.FromCurrentDefinition));
        return version;
    }
}
