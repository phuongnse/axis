using Axis.Worker;

namespace Axis.Integration.Tests;

/// <summary>
/// The next poll time of each tenant, see <c>docs/reference/worker-scheduling.md#waking-a-tenant</c>: the wait of an idle tenant
/// doubles up to the ceiling, an item due sooner brings the next poll forward, and a wake signal resets the wait.
/// </summary>
public sealed class TenantPollsTests
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(8);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void A_tenant_that_is_not_known_yet_is_due_now_and_the_due_ids_come_in_ordinal_order()
    {
        var polls = new TenantPolls(_interval, _ceiling);

        Assert.Equal(["a", "b"], polls.Due(["b", "a"], TimeSpan.Zero));
        Assert.Null(polls.Earliest);
    }

    [Fact]
    public void The_wait_of_an_idle_tenant_doubles_up_to_the_ceiling_and_stays_there()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        var now = TimeSpan.Zero;
        var gaps = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            polls.Idle("a", now, null);
            gaps.Add((polls.Earliest!.Value - now).TotalSeconds);
            now = polls.Earliest.Value;
        }

        Assert.Equal([2d, 4d, 8d, 8d, 8d, 8d], gaps);
    }

    [Fact]
    public void A_tenant_is_not_due_before_its_next_poll_time()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        polls.Idle("a", TimeSpan.Zero, null);

        Assert.Empty(polls.Due(["a"], Seconds(1.9)));
        Assert.Equal(["a"], polls.Due(["a"], Seconds(2)));
    }

    [Fact]
    public void An_item_due_before_the_wait_ends_brings_the_next_poll_forward_and_one_due_later_does_not()
    {
        var polls = new TenantPolls(_interval, _ceiling);

        polls.Idle("a", TimeSpan.Zero, Seconds(0.5));
        Assert.Equal(Seconds(0.5), polls.Earliest);

        polls.Idle("b", TimeSpan.Zero, Seconds(30));
        Assert.Equal(Seconds(0.5), polls.Earliest);
        Assert.Equal(["b"], polls.Due(["b"], Seconds(2)));
        Assert.Empty(polls.Due(["b"], Seconds(1.9)));
    }

    [Fact]
    public void A_poll_that_finds_work_starts_the_wait_over()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        polls.Idle("a", TimeSpan.Zero, null);
        polls.Idle("a", Seconds(2), null);

        polls.Found("a", Seconds(6));

        Assert.Equal(Seconds(7), polls.Earliest);
        polls.Idle("a", Seconds(7), null);
        Assert.Equal(Seconds(9), polls.Earliest);
    }

    [Fact]
    public void A_retry_comes_after_one_poll_interval_and_keeps_the_wait()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        polls.Idle("a", TimeSpan.Zero, null);
        polls.Idle("a", Seconds(2), null);

        polls.Retry("a", Seconds(6));

        Assert.Equal(Seconds(7), polls.Earliest);
        polls.Idle("a", Seconds(7), null);
        Assert.Equal(Seconds(15), polls.Earliest);
    }

    [Fact]
    public void A_reset_starts_the_wait_over_but_keeps_a_sooner_poll_time()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        polls.Idle("a", TimeSpan.Zero, null);
        polls.Idle("a", Seconds(2), null);
        polls.Idle("b", TimeSpan.Zero, Seconds(0.25));

        polls.Reset("a", Seconds(3));
        polls.Reset("b", Seconds(3));

        Assert.Equal(Seconds(0.25), polls.Earliest);
        Assert.Empty(polls.Due(["a"], Seconds(3.9)));
        Assert.Equal(["a"], polls.Due(["a"], Seconds(4)));
        polls.Idle("a", Seconds(4), null);
        Assert.Empty(polls.Due(["a"], Seconds(5.9)));
        Assert.Equal(["a"], polls.Due(["a"], Seconds(6)));
    }

    [Fact]
    public void Resetting_every_tenant_or_forgetting_one_makes_it_due_now()
    {
        var polls = new TenantPolls(_interval, _ceiling);
        polls.Idle("a", TimeSpan.Zero, null);
        polls.Idle("b", TimeSpan.Zero, null);

        polls.Forget("a");
        Assert.Equal(["a"], polls.Due(["a", "b"], Seconds(1)));

        polls.ResetAll();
        Assert.Equal(["a", "b"], polls.Due(["a", "b"], Seconds(1)));
        Assert.Null(polls.Earliest);
    }
}
