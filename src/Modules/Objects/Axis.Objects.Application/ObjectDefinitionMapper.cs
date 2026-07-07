using Axis.Objects.Domain.Aggregates;

namespace Axis.Objects.Application;

internal static class ObjectDefinitionMapper
{
    public static ObjectDefinitionDetailDto ToDetailDto(ObjectDefinition definition)
    {
        ObjectDefinitionVersion? latestVersion = definition.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();

        return new ObjectDefinitionDetailDto(
            definition.Id.Value,
            definition.WorkspaceId,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.DraftVersion,
            definition.LatestPublishedVersionNumber,
            definition.CreatedAt,
            definition.UpdatedAt,
            definition.Fields.OrderBy(field => field.Order).Select(ToFieldDto).ToList(),
            latestVersion is null ? null : ToVersionDto(latestVersion));
    }

    public static ObjectDefinitionListItemDto ToListItemDto(ObjectDefinition definition) =>
        new(
            definition.Id.Value,
            definition.Name,
            definition.Key.Value,
            definition.Status,
            definition.DraftVersion,
            definition.LatestPublishedVersionNumber,
            definition.UpdatedAt);

    public static ObjectDefinitionVersionDto ToVersionDto(ObjectDefinitionVersion version) =>
        new(
            version.Id.Value,
            version.VersionNumber,
            version.PublishedByUserId,
            version.PublishedAt,
            version.Fields.OrderBy(field => field.Order).Select(ToVersionFieldDto).ToList());

    public static IReadOnlyList<ObjectFieldDefinitionSpec> ToDomainSpecs(
        IReadOnlyList<ObjectFieldDefinitionInput> fields) =>
        fields
            .Select((field, index) => new ObjectFieldDefinitionSpec(
                field.FieldKey,
                field.Label,
                index))
            .ToList();

    private static ObjectFieldDefinitionDto ToFieldDto(ObjectFieldDefinition field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order);

    private static ObjectFieldDefinitionDto ToVersionFieldDto(ObjectDefinitionVersionField field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order);
}
