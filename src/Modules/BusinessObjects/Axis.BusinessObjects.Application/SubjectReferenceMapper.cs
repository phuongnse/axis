using Axis.Identity.Contracts;
using DomainSubjectKind = Axis.BusinessObjects.Domain.ValueObjects.SubjectKind;
using DomainSubjectReference = Axis.BusinessObjects.Domain.ValueObjects.SubjectReference;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.BusinessObjects.Application;

internal static class SubjectReferenceMapper
{
    public static DomainSubjectReference ToDomain(IdentitySubjectReference subject) =>
        new((DomainSubjectKind)subject.Kind, subject.Id);

    public static SubjectReferenceDto ToDto(DomainSubjectReference subject) =>
        new((SubjectKind)subject.Kind, subject.Id);
}
