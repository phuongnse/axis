using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.BusinessObjects.Infrastructure.Tests;

public sealed class ResourceMetadataModelTests
{
    [Fact]
    public void BusinessObjectDefinition_WhenModelBuilt_RequiresCompleteResourceProvenance()
    {
        using BusinessObjectsDbContext context = new(
            new DbContextOptionsBuilder<BusinessObjectsDbContext>()
                .UseNpgsql("Host=localhost;Database=axis_business_objects_model;Username=axis;Password=unused")
                .Options);
        IEntityType definition = context.Model.FindEntityType(typeof(BusinessObjectDefinition))!;

        Required(definition, nameof(BusinessObjectDefinition.CreatedAt));
        Required(definition, nameof(BusinessObjectDefinition.UpdatedAt));
        Required(definition, "CreatedByKind");
        Required(definition, "CreatedByDisplayName");
        Required(definition, "UpdatedByKind");
        Required(definition, "UpdatedByDisplayName");
    }

    private static void Required(IEntityType entity, string property) =>
        entity.FindProperty(property)!.IsNullable.Should().BeFalse();
}
