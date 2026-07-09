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
            definition.Revision,
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
            definition.Revision,
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
                index,
                field.FieldType,
                ToDomainVariantSpecs(field.Variants)))
            .ToList();

    private static ObjectFieldDefinitionDto ToFieldDto(ObjectFieldDefinition field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            field.Variants.OrderBy(variant => variant.Order).Select(ToVariantDto).ToList());

    private static ObjectFieldDefinitionDto ToVersionFieldDto(ObjectDefinitionVersionField field) =>
        new(
            field.Id.Value,
            field.Key.Value,
            field.Label,
            field.Order,
            field.FieldType,
            field.Variants.OrderBy(variant => variant.Order).Select(ToVariantDto).ToList());

    private static IReadOnlyList<ObjectFieldVariantSpec> ToDomainVariantSpecs(
        IReadOnlyList<ObjectFieldVariantInput>? variants) =>
        variants?
            .Select(variant => new ObjectFieldVariantSpec(
                variant.Kind,
                variant.MinNumber,
                variant.MaxNumber,
                variant.MinDate,
                variant.MaxDate,
                variant.MinLength,
                variant.MaxLength,
                variant.Pattern,
                variant.Options))
            .ToList()
        ?? [];

    private static ObjectFieldVariantDto ToVariantDto(ObjectFieldVariant variant) =>
        new(
            variant.Kind,
            variant.MinNumber,
            variant.MaxNumber,
            variant.MinDate,
            variant.MaxDate,
            variant.MinLength,
            variant.MaxLength,
            variant.Pattern,
            variant.Options);

    private static ObjectFieldVariantDto ToVariantDto(ObjectDefinitionVersionFieldVariant variant) =>
        new(
            variant.Kind,
            variant.MinNumber,
            variant.MaxNumber,
            variant.MinDate,
            variant.MaxDate,
            variant.MinLength,
            variant.MaxLength,
            variant.Pattern,
            variant.Options);
}
