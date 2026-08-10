using Axis.Identity.Contracts;
using DomainSubjectKind = Axis.Rules.Domain.RuleSubjectKind;
using DomainSubjectReference = Axis.Rules.Domain.RuleSubjectReference;

namespace Axis.Rules.Application;

internal static class RuleSubjectReferenceMapper
{
    public static DomainSubjectReference ToDomain(SubjectReference subject) =>
        new((DomainSubjectKind)subject.Kind, subject.Id);

    public static SubjectReferenceDto ToDto(DomainSubjectReference subject) =>
        new((SubjectKind)subject.Kind, subject.Id);
}
