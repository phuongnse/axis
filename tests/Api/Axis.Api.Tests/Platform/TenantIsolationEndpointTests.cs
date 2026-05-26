using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Platform;

/// <summary>
/// E01 F03 — proves tenant A data is not visible to tenant B via the API (US-008).
/// </summary>
[Collection("Api")]
public sealed class TenantIsolationEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetModel_WhenModelBelongsToAnotherTenant_Returns404()
    {
        HttpClient tenantA = await AuthHelper.CreateAdminClientAsync(fixture, "isoA");
        HttpResponseMessage createResp = await tenantA.PostAsJsonAsync("/api/models", new
        {
            name = "TenantAOnly",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.EnsureSuccessStatusCode();
        string modelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        HttpClient tenantB = await AuthHelper.CreateAdminClientAsync(fixture, "isoB");

        HttpResponseMessage resp = await tenantB.GetAsync($"/api/models/{modelId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetModels_WhenAnotherTenantCreatedModels_DoesNotIncludeTheirModels()
    {
        HttpClient tenantA = await AuthHelper.CreateAdminClientAsync(fixture, "isoC");
        HttpResponseMessage createResp = await tenantA.PostAsJsonAsync("/api/models", new
        {
            name = "HiddenFromB",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.EnsureSuccessStatusCode();

        HttpClient tenantB = await AuthHelper.CreateAdminClientAsync(fixture, "isoD");
        HttpResponseMessage listResp = await tenantB.GetAsync("/api/models");
        listResp.EnsureSuccessStatusCode();

        JsonElement body = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().BeEmpty();
    }
}
