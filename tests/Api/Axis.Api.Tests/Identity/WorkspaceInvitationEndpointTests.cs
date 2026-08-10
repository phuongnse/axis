using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Application;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Legal;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class WorkspaceInvitationEndpointTests(ApiTestFixture fixture)
{
    private const string Password = "maple river sunrise";
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task AT008_WhenAdministratorInvites_RestContractDerivesWorkspaceAndReturnsNonSecretLifecycle()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid workspaceId = await CreateOrganizationWorkspaceAsync();
        await SwitchWorkspaceAsync(workspaceId);
        string recipient = UniqueEmail();

        HttpResponseMessage create = await fixture.PostBrowserJsonAsync(
            "/api/workspace-invitations",
            new { email = recipient, requestedRole = "Member" },
            TestContext.Current.CancellationToken);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await create.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        created.GetProperty("outcome").GetString().Should().Be("Created");
        JsonElement invitation = created.GetProperty("invitation");
        invitation.GetProperty("recipientEmail").GetString().Should().Be(recipient);
        invitation.GetProperty("requestedRole").GetString().Should().Be("Member");
        invitation.GetProperty("status").GetString().Should().Be("Pending");
        created.ToString().Should().NotContain("rawToken");
        created.ToString().Should().NotContain("tokenHash");
        created.ToString().Should().NotContain("deliveryEnvelope");

        JsonElement list = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/workspace-invitations?page=1&pageSize=20",
            Json,
            TestContext.Current.CancellationToken);
        list.GetProperty("items").EnumerateArray()
            .Should().Contain(item =>
                item.GetProperty("invitationId").GetGuid()
                == invitation.GetProperty("invitationId").GetGuid());
    }

    [Fact]
    public async Task AT003_WhenInvitationRoleIsUnsupported_ReturnsFieldCodeWithoutMutation()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid workspaceId = await CreateOrganizationWorkspaceAsync();
        await SwitchWorkspaceAsync(workspaceId);

        HttpResponseMessage response = await fixture.PostBrowserJsonAsync(
            "/api/workspace-invitations",
            new { email = UniqueEmail(), requestedRole = "Owner" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.GetProperty("errorCodes").GetProperty("requestedRole")
            .EnumerateArray().Select(code => code.GetString())
            .Should().Contain(IdentityProblemCodes.InvitationRoleUnsupported);
    }

    [Fact]
    public async Task AcceptJourney_WithMatchedRecipient_CompletesInvitation()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid workspaceId = await CreateOrganizationWorkspaceAsync();
        await SwitchWorkspaceAsync(workspaceId);
        string recipient = UniqueEmail();

        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-invitations",
            new { email = recipient, requestedRole = "Member" },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);
        InvitationDeliveryMessage delivery = await WaitForInvitationDeliveryAsync(recipient);

        (await fixture.PostBrowserAsync(
            "/api/auth/sign-out",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        HttpResponseMessage exchange = await fixture.PostBrowserJsonAsync(
            "/api/internal/workspace-invitations/exchange",
            new { token = delivery.RawToken },
            TestContext.Current.CancellationToken);
        exchange.StatusCode.Should().Be(HttpStatusCode.OK);
        exchange.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant()
            .Should().Contain("__host-axis-invitation-handoff=")
            .And.Contain("httponly")
            .And.Contain("secure")
            .And.NotContain(delivery.RawToken.ToLowerInvariant());

        await CreateVerifiedBrowserSessionAsync(recipient);
        JsonElement review = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/internal/workspace-invitations/review",
            Json,
            TestContext.Current.CancellationToken);
        review.GetProperty("organizationName").GetString().Should().NotBeNullOrWhiteSpace();
        review.GetProperty("workspaceId").GetGuid().Should().Be(workspaceId);
        review.GetProperty("requestedRole").GetString().Should().Be("Member");

        HttpResponseMessage accept = await fixture.PostBrowserAsync(
            "/api/internal/workspace-invitations/accept",
            cancellationToken: TestContext.Current.CancellationToken);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement accepted = await accept.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        accepted.GetProperty("outcome").GetString().Should().Be("Accepted");
        accepted.GetProperty("organizationRole").GetString().Should().Be("Member");
        accepted.GetProperty("workspaceRole").GetString().Should().Be("Member");
        accept.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant()
            .Should().Contain("__host-axis-invitation-handoff=")
            .And.Contain("expires=thu, 01 jan 1970");

        JsonElement eligible = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/workspace-context/eligible",
            Json,
            TestContext.Current.CancellationToken);
        eligible.EnumerateArray().Should().Contain(item =>
            item.GetProperty("workspaceId").GetGuid() == workspaceId);
    }

    [Fact]
    public async Task AT003_WhenInvitationTokenIsInvalid_ReturnsGenericFailureWithoutWorkspaceDisclosure()
    {
        HttpResponseMessage response = await fixture.PostBrowserJsonAsync(
            "/api/internal/workspace-invitations/exchange",
            new { token = "not-an-opaque-token" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.GetProperty("code").GetString().Should().Be(
            IdentityProblemCodes.InvitationAccessInvalid);
        string responseText = body.ToString().ToLowerInvariant();
        responseText.Should().NotContain("workspace");
        responseText.Should().NotContain("organization");
        responseText.Should().NotContain("inviter");
    }

    [Fact]
    public void AcceptanceBootstrapRoutes_WhenOpenApiGenerated_RemainAbsent()
    {
        using IServiceScope scope = fixture.CreateScope();
        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        Microsoft.OpenApi.OpenApiDocument document = provider.GetSwagger("v1");

        document.Paths.Keys.Should().NotContain(path => path.StartsWith(
            "/api/internal/workspace-invitations",
            StringComparison.Ordinal));
    }

    private async Task CreateVerifiedBrowserSessionAsync(string email)
    {
        using HttpRequestMessage register = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Invitation Admin",
                email,
                password = Password,
                passwordConfirmation = Password,
                acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
                acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
            }, options: Json),
        };
        register.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await fixture.SendBrowserMutationAsync(register, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        string token = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException("Verification token was not captured.");
        (await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateOrganizationWorkspaceAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(
                new { name = $"Invitation Organization {Guid.NewGuid():N}" },
                options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage response = await fixture.SendBrowserMutationAsync(
            request,
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        return body.GetProperty("workspaceId").GetGuid();
    }

    private async Task SwitchWorkspaceAsync(Guid workspaceId)
    {
        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = workspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<InvitationDeliveryMessage> WaitForInvitationDeliveryAsync(string email)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            InvitationDeliveryMessage? delivery = fixture.EmailCapture.GetWorkspaceInvitation(email);
            if (delivery is not null)
                return delivery;
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Workspace invitation delivery was not observed.");
    }

    private static string UniqueEmail() => $"workspace-invitation-{Guid.NewGuid():N}@example.com";
}
