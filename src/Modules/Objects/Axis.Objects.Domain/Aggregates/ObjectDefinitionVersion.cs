using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersion : Entity<ObjectDefinitionVersionId>
{
    private readonly List<ObjectDefinitionVersionField> _fields = [];

    public ObjectDefinitionId ObjectDefinitionId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; }
    public ObjectDefinitionKey Key { get; private set; }
    public Guid PublishedByUserId { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public IReadOnlyList<ObjectDefinitionVersionField> Fields => _fields.AsReadOnly();

    private ObjectDefinitionVersion(
        ObjectDefinitionVersionId id,
        ObjectDefinitionId objectDefinitionId,
        Guid workspaceId,
        int versionNumber,
        string name,
        ObjectDefinitionKey key,
        Guid publishedByUserId,
        DateTime publishedAt)
        : base(id)
    {
        ObjectDefinitionId = objectDefinitionId;
        WorkspaceId = workspaceId;
        VersionNumber = versionNumber;
        Name = name;
        Key = key;
        PublishedByUserId = publishedByUserId;
        PublishedAt = publishedAt;
    }

    public static ObjectDefinitionVersion Create(
        ObjectDefinition definition,
        int versionNumber,
        Guid publishedByUserId,
        DateTime publishedAt)
    {
        ObjectDefinitionVersion version = new(
            ObjectDefinitionVersionId.New(),
            definition.Id,
            definition.WorkspaceId,
            versionNumber,
            definition.Name,
            definition.Key,
            publishedByUserId,
            publishedAt);
        version._fields.AddRange(definition.Fields
            .OrderBy(field => field.Order)
            .Select(ObjectDefinitionVersionField.FromDraft));
        return version;
    }
}
