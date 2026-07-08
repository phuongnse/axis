using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Objects.Domain.Tests.Aggregates;

public sealed class ObjectDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime Now = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateUnpublished_WhenDefinitionIdentityIsValid_CreatesEditableUnpublishedDefinitionAtRevisionOne()
    {
        ObjectDefinition definition = CreateUnpublished();

        definition.WorkspaceId.Should().Be(WorkspaceId);
        definition.Name.Should().Be("Customer");
        definition.Key.Value.Should().Be("customer");
        definition.Status.Should().Be(ObjectDefinitionStatus.Unpublished);
        definition.Revision.Should().Be(1);
        definition.Fields.Should().BeEmpty();
    }

    [Fact]
    public void SaveUnpublished_WhenFieldDefinitionsAreValid_PreservesStableFieldsAndIncrementsRevision()
    {
        ObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer Account",
            ValidFields(),
            expectedRevision: 1,
            Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        definition.Name.Should().Be("Customer Account");
        definition.Key.Value.Should().Be("customer");
        definition.Revision.Should().Be(2);
        definition.Fields.Select(field => field.Key.Value)
            .Should().Equal("name", "credit_limit", "opened_on");
        definition.Fields.Select(field => field.Label)
            .Should().Equal("Name", "Credit limit", "Opened on");
    }

    [Fact]
    public void SaveUnpublished_WhenFieldKeyAlreadyExists_ReturnsInvalidInputWithoutChangingUnpublishedDefinition()
    {
        ObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field("name", "Name", order: 0),
                Field("name", "Display name", order: 1),
            ],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        definition.Revision.Should().Be(1);
        definition.Fields.Should().BeEmpty();
    }

    [Fact]
    public void SaveUnpublished_WhenExpectedRevisionIsStale_ReturnsConflictWithoutOverwrite()
    {
        ObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            [Field("name", "Name", order: 0)],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        Result stale = definition.SaveUnpublished(
            "Customer stale",
            [Field("new_name", "New name", order: 0)],
            expectedRevision: 1,
            Now);

        stale.IsFailure.Should().BeTrue();
        stale.ErrorCode.Should().Be(ErrorCodes.Conflict);
        definition.Name.Should().Be("Customer");
        definition.Fields.Should().ContainSingle(field => field.Key.Value == "name");
    }

    [Fact]
    public void SaveUnpublished_WhenFieldLabelIsMissing_ReturnsInvalidInput()
    {
        ObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [Field("name", " ", order: 0)],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    [Fact]
    public void Publish_WhenUnpublishedDefinitionIsValid_CreatesImmutableVersionOneSnapshot()
    {
        ObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            ValidFields(),
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        Result<ObjectDefinitionVersion> result = definition.Publish(
            expectedRevision: 2,
            UserId,
            Now.AddMinutes(2));

        result.IsSuccess.Should().BeTrue();
        ObjectDefinitionVersion version = result.Value;
        version.VersionNumber.Should().Be(1);
        version.Name.Should().Be("Customer");
        version.Key.Value.Should().Be("customer");
        version.PublishedByUserId.Should().Be(UserId);
        version.PublishedAt.Should().Be(Now.AddMinutes(2));
        version.Fields.Select(field => field.Key.Value)
            .Should().Equal("name", "credit_limit", "opened_on");
        version.Fields.Select(field => field.Label)
            .Should().Equal("Name", "Credit limit", "Opened on");
        definition.Status.Should().Be(ObjectDefinitionStatus.Published);
        definition.LatestPublishedVersionNumber.Should().Be(1);
    }

    [Fact]
    public void Publish_WhenUnpublishedDefinitionHasNoFields_ReturnsInvalidInput()
    {
        ObjectDefinition definition = CreateUnpublished();

        Result<ObjectDefinitionVersion> result = definition.Publish(
            expectedRevision: 1,
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    private static ObjectDefinition CreateUnpublished()
    {
        Result<ObjectDefinition> result = ObjectDefinition.CreateUnpublished(
            WorkspaceId,
            "Customer",
            ObjectDefinitionKey.Create("customer").Value,
            Now);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static ObjectFieldDefinitionSpec Field(string key, string label, int order) =>
        new(key, label, order);

    private static IReadOnlyList<ObjectFieldDefinitionSpec> ValidFields() =>
    [
        Field("name", "Name", order: 0),
        Field("credit_limit", "Credit limit", order: 1),
        Field("opened_on", "Opened on", order: 2),
    ];
}
