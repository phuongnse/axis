using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

internal static class RuleActor
{
    public static ActorSnapshot From(ICurrentSubject currentSubject) =>
        currentSubject.Subject.Kind == SubjectKind.Service
            ? ActorSnapshot.ServiceIdentity(currentSubject.Subject.Id, currentSubject.DisplayName)
            : ActorSnapshot.User(currentSubject.Subject.Id, currentSubject.DisplayName);
}
