using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class WorkspaceProductBuilderEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task ManageProductBuilders_WhenRevisionIsCurrent_UsesActiveWorkspaceAndOptimisticRevision()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        User target = User.Create(
            "Product Builder Target",
            Email.Create($"product-builder-{Guid.NewGuid():N}@example.com").Value);
        WorkspaceMembership targetMembership = WorkspaceMembership.CreateOrganizationMember(
            administrator.WorkspaceId,
            target.Id,
            WorkspaceMembershipRole.Member);
        targetMembership.InitializeMetadata(
            ActorSnapshot.User(administrator.UserId, "Workspace Administrator"),
            DateTime.UtcNow);
        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(target);
            db.WorkspaceMemberships.Add(targetMembership);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        JsonElement list = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/workspace-product-builders",
            Json,
            TestContext.Current.CancellationToken);
        JsonElement administratorRow = list.EnumerateArray().Single(
            row => row.GetProperty("userId").GetGuid() == administrator.UserId);
        JsonElement targetRow = list.EnumerateArray().Single(
            row => row.GetProperty("userId").GetGuid() == target.Id);
        administratorRow.GetProperty("canChange").GetBoolean().Should().BeFalse();
        administratorRow.GetProperty("isProductBuilder").GetBoolean().Should().BeTrue();
        targetRow.GetProperty("workspaceRole").GetString().Should().Be("Member");
        targetRow.GetProperty("isProductBuilder").GetBoolean().Should().BeFalse();
        targetRow.GetProperty("canChange").GetBoolean().Should().BeTrue();
        JsonElement targetCreatedBy = targetRow
            .GetProperty("metadata")
            .GetProperty("createdBy");
        targetCreatedBy.GetProperty("kind").GetString().Should().Be("User");
        targetCreatedBy.GetProperty("subjectId").GetGuid().Should().Be(administrator.UserId);
        targetCreatedBy.GetProperty("displayName").GetString()
            .Should().Be("Workspace Administrator");

        HttpResponseMessage granted = await fixture.PostBrowserJsonAsync(
            $"/api/workspace-product-builders/{target.Id}/grant",
            new { expectedRevision = targetRow.GetProperty("membershipRevision").GetInt32() },
            TestContext.Current.CancellationToken);
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement grantBody = await granted.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        grantBody.GetProperty("isProductBuilder").GetBoolean().Should().BeTrue();
        grantBody.GetProperty("membershipRevision").GetInt32().Should().Be(2);

        HttpResponseMessage stale = await fixture.PostBrowserJsonAsync(
            $"/api/workspace-product-builders/{target.Id}/revoke",
            new { expectedRevision = 1 },
            TestContext.Current.CancellationToken);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonElement problem = await stale.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("identity.productBuilder.conflict");

        using IServiceScope readScope = fixture.CreateScope();
        IdentityDbContext readDb = readScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        WorkspaceMembership persisted = await readDb.WorkspaceMemberships.SingleAsync(
            membership => membership.WorkspaceId == administrator.WorkspaceId
                && membership.UserId == target.Id,
            TestContext.Current.CancellationToken);
        persisted.IsProductBuilder.Should().BeTrue();
        string[] auditMetadata = await readDb.IdentityAuditOutboxRecords
            .Where(record => record.WorkspaceId == administrator.WorkspaceId
                && record.Action.StartsWith("workspace.product_builder"))
            .Select(record => record.MetadataJson)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        auditMetadata.Should().NotBeEmpty();
        auditMetadata.Should().OnlyContain(metadata =>
            !metadata.Contains(target.Email.Value, StringComparison.Ordinal)
            && !metadata.Contains(target.FullName, StringComparison.Ordinal));
    }
}
