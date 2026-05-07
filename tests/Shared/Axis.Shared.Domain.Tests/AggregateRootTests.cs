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
    public void New_aggregate_has_no_domain_events()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Raising_event_adds_it_to_domain_events()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(1)
            .And.ContainItemsAssignableTo<TestDomainEvent>();
    }

    [Fact]
    public void ClearDomainEvents_removes_all_events()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_events_are_collected_in_order()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DoSomething();
        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(3);
    }
}
