using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Tenancy;

/// <summary>
/// E2E check that <c>verify-email</c> triggers async multi-module tenant provisioning via
/// Wolverine handlers and that tenant APIs become accessible once the pipeline completes.
///
/// Uses <see cref="KafkaTransportFixture"/> (not <see cref="ApiTestFixture"/>) because the
/// real <c>IdentityUnitOfWork</c> must run so <c>verify-email</c> publishes
/// <c>OrganizationVerifiedEvent</c> and all module <c>OrganizationVerifiedHandler</c>s run.
/// <see cref="ApiTestFixture"/> replaces <c>IUnitOfWork</c> with a no-op so endpoint tests
/// remain deterministic — that no-op would prevent this pipeline from ever starting.
/// </summary>
[Collection("Api-E2E")]
[Trait("Category", "Slow")]
public sealed class TenantProvisioningE2ETests(KafkaTransportFixture fixture)
{
    private static readonly JsonSerializerOptions Json = KafkaTransportFixture.JsonOptions;

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

        bool ready = await PollUntilProvisioningReadyAsync(verifyToken, TimeSpan.FromSeconds(120));
        ready.Should().BeTrue("event-driven tenant provisioning should complete within the timeout");

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
