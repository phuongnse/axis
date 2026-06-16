using axis.datamodeling.events;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Infrastructure.Messaging;
using FluentAssertions;

namespace Axis.DataModeling.Infrastructure.Tests.Messaging;

public class DataModelingEventMapperTests
{
    [Fact]
    public void ToIntegrationEvent_WhenModelCreated_MapsToAvroEvent()
    {
        Guid modelId = Guid.NewGuid();
        Guid WorkspaceId = Guid.NewGuid();

        object? result = DataModelingEventMapper.ToIntegrationEvent(
            new ModelCreated(modelId, WorkspaceId, "Invoice"));

        result.Should().BeOfType<ModelCreatedEvent>();
        ModelCreatedEvent avro = (ModelCreatedEvent)result!;
        avro.modelId.Should().Be(modelId.ToString());
        avro.workspaceId.Should().Be(WorkspaceId.ToString());
        avro.name.Should().Be("Invoice");
    }

    [Fact]
    public void ToIntegrationEvent_WhenFieldAdded_MapsFieldMetadata()
    {
        Guid modelId = Guid.NewGuid();
        Guid WorkspaceId = Guid.NewGuid();
        Guid fieldId = Guid.NewGuid();

        object? result = DataModelingEventMapper.ToIntegrationEvent(
            new FieldAdded(modelId, WorkspaceId, fieldId, "amount", FieldType.Number, "Amount", true, 3));

        result.Should().BeOfType<FieldAddedEvent>();
        FieldAddedEvent avro = (FieldAddedEvent)result!;
        avro.fieldName.Should().Be("amount");
        avro.fieldType.Should().Be(nameof(FieldType.Number));
        avro.isRequired.Should().BeTrue();
        avro.displayOrder.Should().Be(3);
    }

    [Fact]
    public void ToIntegrationEvent_WhenUnknownEvent_ReturnsNull()
    {
        DataModelingEventMapper.ToIntegrationEvent(new UnknownTestDomainEvent()).Should().BeNull();
    }

    private sealed record UnknownTestDomainEvent : Axis.Shared.Domain.Primitives.IDomainEvent;
}
