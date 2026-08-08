using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListAssignableSubjects;

public sealed class ListAssignableSubjectsHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities)
    : IQueryHandler<ListAssignableSubjectsQuery, Result<IReadOnlyList<AssignableSubjectDto>>>
{
    public async Task<Result<IReadOnlyList<AssignableSubjectDto>>> Handle(
        ListAssignableSubjectsQuery query,
        CancellationToken cancellationToken)
    {
        if (!await CreateServiceIdentityHandler.IsAdministrator(
                memberships,
                query.WorkspaceId,
                query.ActorUserId,
                cancellationToken))
            return Result.Failure<IReadOnlyList<AssignableSubjectDto>>(
                ErrorCodes.Forbidden,
                "Active Workspace Administrator membership is required.");

        IReadOnlyList<ActiveWorkspaceHumanProjection> humans =
            await memberships.ListActiveForWorkspaceAsync(query.WorkspaceId, cancellationToken);
        IReadOnlyList<ServiceIdentity> services =
            await identities.ListAsync(query.WorkspaceId, cancellationToken);
        AssignableSubjectDto[] result = humans
            .Select(value => new AssignableSubjectDto(
                SubjectReferenceDto.From(SubjectReference.Human(value.UserId)),
                value.DisplayName,
                value.Email))
            .Concat(services
                .Where(value => value.Status == ServiceIdentityStatus.Active &&
                    value.WorkspaceGrantStatus == ServiceWorkspaceGrantStatus.Active)
                .Select(value => new AssignableSubjectDto(
                    SubjectReferenceDto.From(SubjectReference.Service(value.Id)),
                    value.ClientId,
                    "Service identity")))
            .OrderBy(value => value.Subject.Kind)
            .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.Subject.SubjectId)
            .ToArray();
        return Result.Success<IReadOnlyList<AssignableSubjectDto>>(result);
    }
}
