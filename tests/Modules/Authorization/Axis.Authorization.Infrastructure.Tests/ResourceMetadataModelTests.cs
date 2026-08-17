using Axis.Authorization.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Authorization.Infrastructure.Tests;

public sealed class ResourceMetadataModelTests
{
    [Fact]
    public void ProductRoleAssignment_WhenModelBuilt_RequiresCompleteResourceProvenance()
    {
        using AuthorizationDbContext context = new(
            new DbContextOptionsBuilder<AuthorizationDbContext>()
                .UseNpgsql("Host=localhost;Database=axis_authorization_model;Username=axis;Password=unused")
                .Options);
        IEntityType assignment = context.Model.FindEntityType(typeof(ProductRoleAssignmentRow))!;

        Required(assignment, nameof(ProductRoleAssignmentRow.CreatedAt));
        Required(assignment, nameof(ProductRoleAssignmentRow.UpdatedAt));
        Required(assignment, nameof(ProductRoleAssignmentRow.CreatedByKind));
        Required(assignment, nameof(ProductRoleAssignmentRow.CreatedByDisplayName));
        Required(assignment, nameof(ProductRoleAssignmentRow.UpdatedByKind));
        Required(assignment, nameof(ProductRoleAssignmentRow.UpdatedByDisplayName));
    }

    private static void Required(IEntityType entity, string property) =>
        entity.FindProperty(property)!.IsNullable.Should().BeFalse();
}
