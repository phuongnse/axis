using Axis.Data.Storage;

namespace Axis.Data.Tests;

public sealed class TenantScheduleTests
{
    private static readonly TimeSpan _day = TimeSpan.FromHours(24);

    [Fact]
    public void A_tenant_that_never_ran_is_due_and_gets_the_time_of_the_clock()
    {
        var clock = new TestClock();
        var schedule = new TenantSchedule(_day, clock);

        Assert.True(schedule.TryStart("a", out var now));

        Assert.Equal(clock.GetUtcNow(), now);
    }

    [Fact]
    public void A_tenant_is_not_due_again_before_the_interval_has_passed()
    {
        var clock = new TestClock();
        var schedule = new TenantSchedule(_day, clock);
        schedule.TryStart("a", out _);

        clock.Advance(_day - TimeSpan.FromSeconds(1));

        Assert.False(schedule.TryStart("a", out _));
    }

    [Fact]
    public void A_tenant_is_due_again_once_the_interval_has_passed_and_then_waits_for_the_next_interval()
    {
        var clock = new TestClock();
        var schedule = new TenantSchedule(_day, clock);
        schedule.TryStart("a", out _);

        clock.Advance(_day);
        Assert.True(schedule.TryStart("a", out _));

        clock.Advance(TimeSpan.FromHours(23));
        Assert.False(schedule.TryStart("a", out _));
    }

    [Fact]
    public void Each_tenant_has_its_own_schedule()
    {
        var clock = new TestClock();
        var schedule = new TenantSchedule(_day, clock);
        schedule.TryStart("a", out _);

        clock.Advance(TimeSpan.FromHours(12));

        Assert.True(schedule.TryStart("b", out _));
        Assert.False(schedule.TryStart("a", out _));
    }

    [Fact]
    public void A_forgotten_tenant_is_due_at_once()
    {
        var clock = new TestClock();
        var schedule = new TenantSchedule(_day, clock);
        schedule.TryStart("a", out _);

        schedule.Forget("a");

        Assert.True(schedule.TryStart("a", out _));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;
    }
}
