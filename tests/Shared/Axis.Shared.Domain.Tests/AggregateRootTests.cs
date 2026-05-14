using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class AggregateRootTests
{
    private record TestDomainEvent(Guid AggregateId) : IDomainEvent;

    private class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void DoSomething() => RaiseDomainEvent(new TestDomainEvent(Id));
    }

    [Fact]
    public void AggregateRoot_WhenCreated_HasNoDomainEvents()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AggregateRoot_WhenEventRaised_AddsEventToDomainEvents()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(1)
            .And.ContainItemsAssignableTo<TestDomainEvent>();
    }

    [Fact]
    public void ClearDomainEvents_WhenCalled_RemovesAllEvents()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AggregateRoot_WhenMultipleEventsRaised_CollectsAllInOrder()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DoSomething();
        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(3);
    }
}
