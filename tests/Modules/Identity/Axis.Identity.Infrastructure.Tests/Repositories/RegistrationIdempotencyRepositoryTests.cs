using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public class RegistrationIdempotencyRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _ctx = null!;
    private RegistrationIdempotencyRepository _sut = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new RegistrationIdempotencyRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AcquireAsync_WhenNoRowExists_InsertsPendingAndReturnsAcquired()
    {
        RegistrationIdempotencyAcquireResult result = await _sut.AcquireAsync("new-key");

        result.Should().Be(RegistrationIdempotencyAcquireResult.Acquired);
        RegistrationIdempotencyRecord? row = await _ctx.Set<RegistrationIdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == "new-key");
        row.Should().NotBeNull();
        row!.Status.Should().Be(RegistrationIdempotencyStatus.Pending);
    }

    [Fact]
    public async Task AcquireAsync_WhenPendingWithinLease_ReturnsInProgress()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ctx.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = "in-flight",
            Status = RegistrationIdempotencyStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        RegistrationIdempotencyAcquireResult result = await _sut.AcquireAsync("in-flight");

        result.Should().Be(RegistrationIdempotencyAcquireResult.InProgress);
    }

    [Fact]
    public async Task AcquireAsync_WhenPendingLeaseExpired_ReturnsAcquiredAndRefreshesUpdatedAt()
    {
        DateTimeOffset stale = DateTimeOffset.UtcNow.AddMinutes(-20);
        _ctx.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = "stale-pending",
            Status = RegistrationIdempotencyStatus.Pending,
            CreatedAt = stale,
            UpdatedAt = stale,
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        RegistrationIdempotencyAcquireResult result = await _sut.AcquireAsync("stale-pending");

        result.Should().Be(RegistrationIdempotencyAcquireResult.Acquired);
        RegistrationIdempotencyRecord row = await _ctx.Set<RegistrationIdempotencyRecord>()
            .SingleAsync(r => r.IdempotencyKey == "stale-pending");
        row.Status.Should().Be(RegistrationIdempotencyStatus.Pending);
        row.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcquireAsync_WhenCompleted_ReturnsAlreadyCompleted()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ctx.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = "done",
            Status = RegistrationIdempotencyStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        RegistrationIdempotencyAcquireResult result = await _sut.AcquireAsync("done");

        result.Should().Be(RegistrationIdempotencyAcquireResult.AlreadyCompleted);
    }

    [Fact]
    public async Task AcquireAsync_WhenFailed_ReturnsAcquiredAndSetsPending()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ctx.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = "retry",
            Status = RegistrationIdempotencyStatus.Failed,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        RegistrationIdempotencyAcquireResult result = await _sut.AcquireAsync("retry");

        result.Should().Be(RegistrationIdempotencyAcquireResult.Acquired);
        RegistrationIdempotencyRecord row = await _ctx.Set<RegistrationIdempotencyRecord>()
            .SingleAsync(r => r.IdempotencyKey == "retry");
        row.Status.Should().Be(RegistrationIdempotencyStatus.Pending);
    }

    [Fact]
    public async Task MarkFailedAsync_WhenOtherEntitiesAreTracked_DoesNotPersistUnrelatedChanges()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ctx.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = "failed-with-tracked-user",
            Status = RegistrationIdempotencyStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _ctx.SaveChangesAsync();

        User unrelated = User.Create(
            "Pending",
            "User",
            Email.Create("pending-user@example.com").Value!);
        _ctx.Users.Add(unrelated);

        await _sut.MarkFailedAsync("failed-with-tracked-user");

        _ctx.ChangeTracker.Clear();
        RegistrationIdempotencyRecord row = await _ctx.Set<RegistrationIdempotencyRecord>()
            .SingleAsync(r => r.IdempotencyKey == "failed-with-tracked-user");
        row.Status.Should().Be(RegistrationIdempotencyStatus.Failed);
        bool userPersisted = await _ctx.Users.AnyAsync(u => u.Email == Email.Create("pending-user@example.com").Value);
        userPersisted.Should().BeFalse();
    }
}
