using Axis.Identity.Contracts;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListAssignableSubjects;

public sealed record AssignableSubjectDto(
    SubjectReferenceDto Subject,
    string DisplayName,
    string? SecondaryLabel);

public sealed record ListAssignableSubjectsQuery(
    Guid ActorUserId,
    Guid WorkspaceId) : IQuery<Result<IReadOnlyList<AssignableSubjectDto>>>;
