using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectActor
{
    public static ActorSnapshot From(ICurrentSubject currentSubject) =>
        currentSubject.Subject.Kind == SubjectKind.Service
            ? ActorSnapshot.ServiceIdentity(currentSubject.Subject.Id, currentSubject.DisplayName)
            : ActorSnapshot.User(currentSubject.Subject.Id, currentSubject.DisplayName);
}
