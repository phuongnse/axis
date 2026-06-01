using Axis.Identity.Application.Queries.GetLegalVersions;
using Axis.Identity.Domain.Legal;
using FluentAssertions;

namespace Axis.Identity.Application.Tests.Queries;

public class GetLegalVersionsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsCurrentWellKnownVersions()
    {
        LegalVersionsDto result = await new GetLegalVersionsHandler().Handle(
            new GetLegalVersionsQuery(),
            CancellationToken.None);

        result.TermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        result.PrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
    }
}
