using System.ComponentModel.DataAnnotations;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;

namespace Axis.Identity.Application;

public sealed record ServiceIdentityKeyDto(Guid Id, string Kid, string Thumbprint, string Status);
public sealed record ServiceIdentityDto(Guid Id, string ClientId, Guid WorkspaceId, string Status, string WorkspaceGrantStatus, int Revision, IReadOnlyList<ServiceIdentityKeyDto> Keys, [property: Required] ResourceMetadataDto Metadata)
{
    public SubjectReferenceDto Subject => SubjectReferenceDto.From(SubjectReference.Service(Id));
}
internal static class ServiceIdentityDtoMapping
{
    public static ServiceIdentityDto ToDto(this ServiceIdentity identity) => new(identity.Id, identity.ClientId, identity.WorkspaceId, identity.Status.ToString(), identity.WorkspaceGrantStatus.ToString(), identity.Revision, identity.Keys.OrderBy(x => x.Kid, StringComparer.Ordinal).Select(x => new ServiceIdentityKeyDto(x.Id, x.Kid, x.Thumbprint, x.Status.ToString())).ToArray(), ResourceMetadataMapping.From(identity.Revision, identity.CreatedBy, identity.CreatedAt, identity.UpdatedBy, identity.UpdatedAt));
}
