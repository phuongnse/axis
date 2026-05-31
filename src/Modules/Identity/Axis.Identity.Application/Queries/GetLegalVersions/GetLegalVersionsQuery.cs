using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetLegalVersions;

public record GetLegalVersionsQuery : IQuery<LegalVersionsDto>;

public sealed record LegalVersionsDto(string TermsVersion, string PrivacyVersion);
