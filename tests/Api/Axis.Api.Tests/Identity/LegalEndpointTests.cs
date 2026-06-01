using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class LegalEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetLegalVersions_ReturnsCurrentDocumentVersions()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/legal/versions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("terms_version").GetString().Should().Be(WellKnownLegalDocuments.TermsVersion);
        body.GetProperty("privacy_version").GetString().Should().Be(WellKnownLegalDocuments.PrivacyVersion);
    }
}
