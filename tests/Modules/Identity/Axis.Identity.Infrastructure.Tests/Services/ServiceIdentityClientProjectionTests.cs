using System.Text.Json;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public sealed class ServiceIdentityClientProjectionTests(IdentityDatabaseFixture database)
    : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await DeleteApplicationsAsync();

    public async ValueTask DisposeAsync() => await DeleteApplicationsAsync();

    [Fact]
    public async Task Projection_WhenKeyLifecycleChanges_UpdatesConfidentialClient()
    {
        await using IdentityDbContext context = database.CreateContext();
        ServiceIdentity identity = ServiceIdentity.Create(
            Guid.NewGuid(),
            "service-projection",
            DateTime.UtcNow);
        identity.AddKey(
            "key-1",
            "thumbprint-1",
            new string('A', 43),
            new string('B', 43),
            expectedRevision: 1,
            DateTime.UtcNow);
        ServiceIdentityClientProjection projection = new ServiceIdentityClientProjection(context);

        await projection.StageAsync(identity, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        OpenIddictEntityFrameworkCoreApplication application = await context
            .Set<OpenIddictEntityFrameworkCoreApplication>()
            .SingleAsync(
                value => value.ClientId == identity.ClientId,
                TestContext.Current.CancellationToken);
        application.ClientType.Should().Be(ClientTypes.Confidential);
        JsonSerializer.Deserialize<string[]>(application.Permissions!).Should().Contain(
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.ClientCredentials);
        using (JsonDocument keys = JsonDocument.Parse(application.JsonWebKeySet!))
        {
            JsonElement key = keys.RootElement.GetProperty("keys").EnumerateArray().Single();
            key.GetProperty("kid").GetString().Should().Be("key-1");
            key.GetProperty("alg").GetString().Should().Be("ES256");
            key.TryGetProperty("d", out _).Should().BeFalse();
        }

        identity.RevokeKey(
            identity.Keys.Single().Id,
            expectedRevision: 2,
            DateTime.UtcNow.AddMinutes(1));
        await projection.StageAsync(identity, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        application = await context
            .Set<OpenIddictEntityFrameworkCoreApplication>()
            .SingleAsync(
                value => value.ClientId == identity.ClientId,
                TestContext.Current.CancellationToken);
        using JsonDocument revokedKeys = JsonDocument.Parse(application.JsonWebKeySet!);
        revokedKeys.RootElement.GetProperty("keys").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Projection_WhenClientOwnedElsewhere_RejectsCollision()
    {
        await using IdentityDbContext context = database.CreateContext();
        await context.Set<OpenIddictEntityFrameworkCoreApplication>().AddAsync(
            new OpenIddictEntityFrameworkCoreApplication
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = "external-client",
                ClientType = ClientTypes.Public,
                ConcurrencyToken = Guid.NewGuid().ToString(),
                Properties = "{}",
            },
            TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        ServiceIdentityClientProjection projection = new ServiceIdentityClientProjection(context);
        ServiceIdentity identity = ServiceIdentity.Create(
            Guid.NewGuid(),
            "external-client",
            DateTime.UtcNow);

        Func<Task> act = () => projection.StageAsync(
            identity,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ServiceIdentityClientProjectionException>();
        context.Entry(
            await context.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync(
                TestContext.Current.CancellationToken)).State.Should().Be(EntityState.Unchanged);
    }

    private async Task DeleteApplicationsAsync()
    {
        await using IdentityDbContext context = database.CreateContext();
        await context.Set<OpenIddictEntityFrameworkCoreApplication>()
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }
}
