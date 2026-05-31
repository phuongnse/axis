using Axis.Identity.Domain.Legal;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetLegalVersions;

public sealed class GetLegalVersionsHandler : IQueryHandler<GetLegalVersionsQuery, LegalVersionsDto>
{
    public Task<LegalVersionsDto> Handle(GetLegalVersionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new LegalVersionsDto(
            WellKnownLegalDocuments.TermsVersion,
            WellKnownLegalDocuments.PrivacyVersion));
}
