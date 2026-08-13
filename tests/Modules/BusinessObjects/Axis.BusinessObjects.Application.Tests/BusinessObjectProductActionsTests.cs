using Axis.Authorization.Contracts;
using FluentAssertions;

namespace Axis.BusinessObjects.Application.Tests;

public sealed class BusinessObjectProductActionsTests
{
    [Fact]
    public void Descriptors_WhenEnumerated_MatchPublishedContract()
    {
        BusinessObjectProductActions.Descriptors.Should().BeEquivalentTo(
        [
            new ProductActionDescriptor("business-object.definition.read-published", "business-object.definition", ProductActionKind.NonRecord),
            new ProductActionDescriptor("business-object.record.create", "business-object.record", ProductActionKind.Record),
            new ProductActionDescriptor("business-object.record.list", "business-object.record", ProductActionKind.Record),
            new ProductActionDescriptor("business-object.record.read", "business-object.record", ProductActionKind.Record),
            new ProductActionDescriptor("business-object.record.save", "business-object.record", ProductActionKind.Record),
            new ProductActionDescriptor("business-object.record.submit", "business-object.record", ProductActionKind.Record),
        ], options => options.WithStrictOrdering());
    }
}
