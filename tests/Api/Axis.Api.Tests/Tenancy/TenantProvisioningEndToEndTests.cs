using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Tenancy;

/// <summary>
/// End-to-end check that verify-email triggers async module provisioning (Kafka/RabbitMQ)
/// and tenant APIs work without <see cref="ApiTestFixture.EnsureTenantProvisionedAsync"/>.
/// </summary>
[Collection("Api")]
[Trait("Category", "Slow")]
public sealed class TenantProvisioningEndToEndTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task RegisterAndVerify_WhenEventPipelineCompletes_AllowsTenantApiAccess()
    {
        string suffix = $"e2e-{Guid.NewGuid():N}"[..12];
        string email = $"admin{suffix}@test.com";

        HttpResponseMessage regResp = await fixture.Client.PostAsJsonAsync("/api/organizations", new
        {
            org_name = $"TestOrg{suffix}",
            admin_first_name = "Test",
            admin_last_name = "Admin",
            admin_email = email,
            password = "TestPass1",
            password_confirmation = "TestPass1",
        }, Json);
        regResp.IsSuccessStatusCode.Should().BeTrue();

        string verifyToken = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException($"No verification token for {email}.");

        HttpResponseMessage verifyResp = await fixture.Client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token = verifyToken },
            Json);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        bool ready = await PollUntilProvisioningReadyAsync(verifyToken, TimeSpan.FromSeconds(90));
        ready.Should().BeTrue("event-driven tenant provisioning should complete in the API test host");

        HttpClient pkceClient = fixture.CreateNewClient();
        string accessToken = await AuthHelper.CompletePkceFlowAsync(pkceClient, email, "TestPass1");

        HttpClient authedClient = fixture.CreateNewClient();
        authedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage modelsResp = await authedClient.GetAsync("/api/models");
        modelsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<bool> PollUntilProvisioningReadyAsync(string verifyToken, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        string path = "/api/auth/provisioning-status?token=" + Uri.EscapeDataString(verifyToken);

        while (DateTimeOffset.UtcNow < deadline)
        {
            HttpResponseMessage resp = await fixture.Client.GetAsync(path);
            if (resp.IsSuccessStatusCode)
            {
                JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
                if (body.TryGetProperty("is_ready", out JsonElement isReady) && isReady.GetBoolean())
                    return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }
}
