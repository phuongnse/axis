using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Solutions.Infrastructure.Tests;

public sealed class ResourceMetadataModelTests
{
    [Fact]
    public void SolutionResources_WhenModelBuilt_RequireCompleteResourceProvenance()
    {
        using SolutionsDbContext context = new(
            new DbContextOptionsBuilder<SolutionsDbContext>()
                .UseNpgsql("Host=localhost;Database=axis_solutions_model;Username=axis;Password=unused")
                .Options);

        IEntityType version = context.Model.FindEntityType(typeof(SolutionVersion))!;
        Required(version, nameof(SolutionVersion.PublishedAt));
        Required(version, "CreatedByKind");
        Required(version, "CreatedByDisplayName");

        IEntityType installation = context.Model.FindEntityType(typeof(SolutionInstallation))!;
        Required(installation, nameof(SolutionInstallation.CreatedAt));
        Required(installation, nameof(SolutionInstallation.UpdatedAt));
        Required(installation, "CreatedByKind");
        Required(installation, "CreatedByDisplayName");
        Required(installation, "UpdatedByKind");
        Required(installation, "UpdatedByDisplayName");
    }

    private static void Required(IEntityType entity, string property) =>
        entity.FindProperty(property)!.IsNullable.Should().BeFalse();
}
