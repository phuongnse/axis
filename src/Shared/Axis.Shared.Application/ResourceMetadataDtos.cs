using System.ComponentModel.DataAnnotations;
using Axis.Shared.Domain.Primitives;

namespace Axis.Shared.Application;

public sealed record ResourceActorDto(
    [property: Required] ActorKind Kind,
    Guid? SubjectId,
    [property: Required] string DisplayName)
{
    public static ResourceActorDto From(ActorSnapshot actor) =>
        new(actor.Kind, actor.SubjectId, actor.DisplayName);
}

public sealed record ResourceMetadataDto(
    long? Revision,
    ResourceActorDto? CreatedBy,
    DateTimeOffset? CreatedAt,
    ResourceActorDto? ModifiedBy,
    DateTimeOffset? ModifiedAt);

public static class ResourceMetadataMapping
{
    public static ResourceMetadataDto From(
        long? revision,
        ActorSnapshot? createdBy,
        DateTimeOffset? createdAt,
        ActorSnapshot? modifiedBy,
        DateTimeOffset? modifiedAt) =>
        new(
            revision,
            createdBy is { } created ? ResourceActorDto.From(created) : null,
            createdAt,
            modifiedBy is { } modified ? ResourceActorDto.From(modified) : null,
            modifiedAt);

    public static ResourceMetadataDto From(
        long? revision,
        ActorSnapshot? createdBy,
        DateTime? createdAt,
        ActorSnapshot? modifiedBy,
        DateTime? modifiedAt) =>
        new(
            revision,
            createdBy is { } created ? ResourceActorDto.From(created) : null,
            createdAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(createdAt.Value, DateTimeKind.Utc)) : null,
            modifiedBy is { } modified ? ResourceActorDto.From(modified) : null,
            modifiedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(modifiedAt.Value, DateTimeKind.Utc)) : null);
}
