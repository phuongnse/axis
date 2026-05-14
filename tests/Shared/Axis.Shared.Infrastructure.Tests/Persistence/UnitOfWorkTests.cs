using Axis.Shared.Domain.Primitives;
using Axis.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Wolverine;

namespace Axis.Shared.Infrastructure.Tests.Persistence;

public class UnitOfWorkTests : IAsyncLifetime
{
    // ── Test fixtures ──────────────────────────────────────────────────────

    private sealed class OrderAggregate : AggregateRoot<Guid>
    {
        public string Name { get; private set; }
        private OrderAggregate() : base(Guid.NewGuid()) { Name = ""; }

        public static OrderAggregate Create(string name)
        {
            OrderAggregate agg = new() { Name = name };
            agg.RaiseDomainEvent(new OrderCreatedEvent(agg.Id, name));
            return agg;
        }

        public new void RaiseDomainEvent(IDomainEvent evt) => base.RaiseDomainEvent(evt);
    }

    private record OrderCreatedEvent(Guid OrderId, string Name) : IDomainEvent;

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

    private sealed class TestUnitOfWork(DbContext ctx, IMessageBus bus) : UnitOfWork(ctx, bus);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private PostgreSqlContainer _postgres = null!;
    private TestDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().Build();
        await _postgres.StartAsync();

        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _context = new TestDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task ResetAsync() =>
        await _context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"Orders\" RESTART IDENTITY CASCADE");

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChangesAsync_WhenAggregateHasDomainEvents_DispatchesEvents()
    {
        await ResetAsync();
        IMessageBus bus = Substitute.For<IMessageBus>();
        TestUnitOfWork uow = new(_context, bus);

        OrderAggregate order = OrderAggregate.Create("Widget");
        await _context.Orders.AddAsync(order);

        await uow.SaveChangesAsync();

        await bus.Received(1).PublishAsync(
            Arg.Is<OrderCreatedEvent>(e => e.OrderId == order.Id),
            Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEventsDispatched_ClearsDomainEvents()
    {
        await ResetAsync();
        IMessageBus bus = Substitute.For<IMessageBus>();
        TestUnitOfWork uow = new(_context, bus);

        OrderAggregate order = OrderAggregate.Create("Widget");
        await _context.Orders.AddAsync(order);

        await uow.SaveChangesAsync();

        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoEvents_DoesNotDispatch()
    {
        await ResetAsync();
        IMessageBus bus = Substitute.For<IMessageBus>();
        TestUnitOfWork uow = new(_context, bus);

        await uow.SaveChangesAsync();

        await bus.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<DeliveryOptions?>());
    }
}
