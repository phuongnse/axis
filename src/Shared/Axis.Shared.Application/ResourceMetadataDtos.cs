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
    [property: Required] ResourceActorDto CreatedBy,
    [property: Required] DateTimeOffset CreatedAt,
    [property: Required] ResourceActorDto ModifiedBy,
    [property: Required] DateTimeOffset ModifiedAt);

public static class ResourceMetadataMapping
{
    public static ResourceMetadataDto From(
        long? revision,
        ActorSnapshot createdBy,
        DateTimeOffset createdAt,
        ActorSnapshot modifiedBy,
        DateTimeOffset modifiedAt) =>
        new(
            revision,
            ResourceActorDto.From(createdBy),
            createdAt,
            ResourceActorDto.From(modifiedBy),
            modifiedAt);

    public static ResourceMetadataDto From(
        long? revision,
        ActorSnapshot createdBy,
        DateTime createdAt,
        ActorSnapshot modifiedBy,
        DateTime modifiedAt) =>
        new(
            revision,
            ResourceActorDto.From(createdBy),
            new DateTimeOffset(DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)),
            ResourceActorDto.From(modifiedBy),
            new DateTimeOffset(DateTime.SpecifyKind(modifiedAt, DateTimeKind.Utc)));
}
