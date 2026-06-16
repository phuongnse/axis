using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Tenancy;

[Collection("Api")]
public sealed class TenantIsolationEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetModel_WhenModelBelongsToAnotherTenant_Returns404()
    {
        HttpClient tenantA = await AuthHelper.CreateAdminClientAsync(fixture, "iso-a");
        HttpClient tenantB = await AuthHelper.CreateAdminClientAsync(fixture, "iso-b");

        HttpResponseMessage createResp = await tenantA.PostAsJsonAsync("/api/models", new
        {
            name = "TenantAOnly",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string modelId = created.GetProperty("id").GetString()!;

        HttpResponseMessage crossRead = await tenantB.GetAsync($"/api/models/{modelId}");

        crossRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetModels_WhenAnotherTenantCreatedModels_ReturnsEmptyList()
    {
        HttpClient tenantA = await AuthHelper.CreateAdminClientAsync(fixture, "iso-c");
        HttpClient tenantB = await AuthHelper.CreateAdminClientAsync(fixture, "iso-d");

        HttpResponseMessage createResp = await tenantA.PostAsJsonAsync("/api/models", new
        {
            name = "PrivateModel",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage listResp = await tenantB.GetAsync("/api/models");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetModels_WhenTeamAccountIsArchived_Returns403()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "iso-archived");
        Guid teamAccountId = await GetTeamAccountIdForEmailAsync("adminiso-archived@test.com");
        await SetTeamAccountStatusAsync(teamAccountId, TeamAccountStatus.Archived);

        HttpResponseMessage resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetTeamAccountIdForEmailAsync(string emailAddress)
    {
        Email email = Email.Create(emailAddress).Value!;
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        TeamAccountMembership membership = await db.TeamAccountMemberships
            .AsNoTracking()
            .SingleAsync(m => m.UserId == user.Id);
        return membership.TeamAccountId;
    }

    private async Task SetTeamAccountStatusAsync(Guid teamAccountId, TeamAccountStatus status)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        TeamAccount teamAccount = await db.TeamAccounts.SingleAsync(o => o.Id == teamAccountId);

        switch (status)
        {
            case TeamAccountStatus.Archived:
                if (teamAccount.Status == TeamAccountStatus.Provisioning)
                    teamAccount.CompleteProvisioning();
                teamAccount.Archive();
                break;
            default:
                throw new NotSupportedException($"Test helper does not set status {status}.");
        }

        await db.SaveChangesAsync();
    }
}
