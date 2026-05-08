using Axis.Shared.Domain.Primitives;
using Axis.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wolverine;

namespace Axis.Shared.Infrastructure.Tests.Persistence;

public class UnitOfWorkTests
{
    // Minimal in-memory aggregate for testing
    private sealed class OrderAggregate : AggregateRoot<Guid>
    {
        public string Name { get; private set; }
        private OrderAggregate() : base(Guid.NewGuid()) { Name = ""; }

        public static OrderAggregate Create(string name)
        {
            var agg = new OrderAggregate { Name = name };
            agg.RaiseDomainEvent(new OrderCreatedEvent(agg.Id, name));
            return agg;
        }

        // Expose for tests
        public new void RaiseDomainEvent(IDomainEvent evt) => base.RaiseDomainEvent(evt);
    }

    private record OrderCreatedEvent(Guid OrderId, string Name) : IDomainEvent;

    // Minimal in-memory DbContext
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public DbSet<OrderAggregate> Orders => Set<OrderAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderAggregate>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.Name);
            });
        }
    }

    private static TestDbContext BuildInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_dispatches_domain_events_raised_by_aggregates()
    {
        await using var ctx = BuildInMemoryContext();
        var bus = Substitute.For<IMessageBus>();
        var uow = new TestUnitOfWork(ctx, bus);

        var order = OrderAggregate.Create("Widget");
        await ctx.Orders.AddAsync(order);

        await uow.SaveChangesAsync();

        await bus.Received(1).PublishAsync(
            Arg.Is<OrderCreatedEvent>(e => e.OrderId == order.Id),
            Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task SaveChangesAsync_clears_domain_events_after_dispatch()
    {
        await using var ctx = BuildInMemoryContext();
        var bus = Substitute.For<IMessageBus>();
        var uow = new TestUnitOfWork(ctx, bus);

        var order = OrderAggregate.Create("Widget");
        await ctx.Orders.AddAsync(order);

        await uow.SaveChangesAsync();

        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_does_not_dispatch_when_no_events()
    {
        await using var ctx = BuildInMemoryContext();
        var bus = Substitute.For<IMessageBus>();
        var uow = new TestUnitOfWork(ctx, bus);

        // Add without raising events (direct set via EF shadow state isn't possible easily,
        // so just don't attach any aggregates)

        await uow.SaveChangesAsync();

        await bus.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<DeliveryOptions?>());
    }

    // Concrete subclass to test the abstract base
    private sealed class TestUnitOfWork(DbContext ctx, IMessageBus bus)
        : UnitOfWork(ctx, bus);
}
