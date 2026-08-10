using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.Rules.Application;
using FluentAssertions;

namespace Axis.Api.Tests.Navigation;

[Collection("Api")]
public sealed class ModuleNavigationEndpointTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ModuleNavigation_WhenAuthorityChanges_ReturnsOnlyServerAvailableContributions()
    {
        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        List<ProductAuthorizationRequest> requests = [];
        await fixture.SetProductAuthorizationTestDecisionAsync(
            request =>
            {
                requests.Add(request);
                return request.ActionKey switch
                {
                    BusinessObjectProductActions.DefinitionRead => ProductAuthorizationDecision.Denied,
                    BusinessObjectProductActions.DefinitionReadPublished =>
                        new ProductAuthorizationDecision(true, ProductActionScope.None),
                    RuleProductActions.DefinitionRead =>
                        new ProductAuthorizationDecision(true, ProductActionScope.None),
                    _ => ProductAuthorizationDecision.Denied,
                };
            },
            TestContext.Current.CancellationToken);

        HttpResponseMessage availableResponse = await fixture.Client.GetAsync(
            "/api/module-navigation",
            TestContext.Current.CancellationToken);

        availableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement available = await availableResponse.Content.ReadFromJsonAsync<JsonElement>(
            ApiTestFixture.JsonOptions,
            TestContext.Current.CancellationToken);
        available.GetProperty("availableContributionIds").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(
                "identity.memberships",
                "identity.service-identities",
                "authorization.product-roles",
                "businessObjects.definitions",
                "rules.fieldDefinitions",
                "solutions.management");
        requests.Select(request => request.ActionKey).Should().Equal(
            BusinessObjectProductActions.DefinitionRead,
            BusinessObjectProductActions.DefinitionReadPublished,
            RuleProductActions.DefinitionRead);
        requests.Should().AllSatisfy(request => request.ResourceKey.Should().BeNull());

        await fixture.SetProductAuthorizationTestDecisionAsync(
            request => request.ActionKey == RuleProductActions.DefinitionRead
                ? ProductAuthorizationDecision.Unavailable
                : ProductAuthorizationDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage deniedResponse = await fixture.Client.GetAsync(
            "/api/module-navigation",
            TestContext.Current.CancellationToken);

        deniedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement denied = await deniedResponse.Content.ReadFromJsonAsync<JsonElement>(
            ApiTestFixture.JsonOptions,
            TestContext.Current.CancellationToken);
        denied.GetProperty("availableContributionIds").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(
                "identity.memberships",
                "identity.service-identities",
                "authorization.product-roles",
                "solutions.management");
    }

    [Fact]
    public async Task ModuleNavigation_WhenPersonalOwner_ReturnsLifecycleSurfacesWithoutMembershipInvitations()
    {
        await PersonalWorkspaceOwnerApiTestSession.CreateAsync(fixture);
        await fixture.SetProductAuthorizationTestDecisionAsync(
            _ => ProductAuthorizationDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await fixture.Client.GetAsync(
            "/api/module-navigation",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            ApiTestFixture.JsonOptions,
            TestContext.Current.CancellationToken);
        body.GetProperty("availableContributionIds").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(
                "identity.service-identities",
                "authorization.product-roles",
                "solutions.management");
    }

    [Fact]
    public async Task ModuleNavigation_WhenAnonymous_ReturnsUnauthorized()
    {
        using HttpClient anonymous = fixture.CreateAnonymousClient();

        HttpResponseMessage response = await anonymous.GetAsync(
            "/api/module-navigation",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
