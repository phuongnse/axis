using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Identity.Infrastructure.Tests;

public sealed class ResourceMetadataModelTests
{
    [Fact]
    public void ManagedIdentityResources_WhenModelBuilt_RequireCompleteResourceProvenance()
    {
        using IdentityDbContext context = new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=axis_identity_model;Username=axis;Password=unused")
                .UseOpenIddict()
                .Options);

        AssertRequired(context.Model.FindEntityType(typeof(WorkspaceMembership))!, timestamps: true);
        AssertRequired(context.Model.FindEntityType(typeof(WorkspaceInvitation))!, timestamps: true);
        AssertRequired(context.Model.FindEntityType(typeof(ServiceIdentity))!, timestamps: true);
    }

    private static void AssertRequired(IEntityType entity, bool timestamps)
    {
        if (timestamps)
        {
            Required(entity, "CreatedAt");
            Required(entity, "UpdatedAt");
        }
        Required(entity, "CreatedByKind");
        Required(entity, "CreatedByDisplayName");
        Required(entity, "UpdatedByKind");
        Required(entity, "UpdatedByDisplayName");
    }

    private static void Required(IEntityType entity, string property) =>
        entity.FindProperty(property)!.IsNullable.Should().BeFalse();
}
