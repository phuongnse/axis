using Axis.Worker;

namespace Axis.Integration.Tests;

/// <summary>
/// The active list of the worker hands out tenants by weighted deficit round-robin, see
/// <c>docs/reference/worker-scheduling.md#fair-share</c>. The test covers a tenant whose claim waits for a call lease.
/// </summary>
public sealed class TenantRoundsTests
{
    private const int Quantum = 10;

    [Fact]
    public void A_tenant_that_waited_across_several_polls_runs_no_more_than_one_quantum_before_another_tenant()
    {
        var rounds = new TenantRounds(Quantum, _ => 1);
        rounds.Join(["a", "b"]);

        // Tenant a waits for a lease in five polls. Each poll joins it again and starts a new turn.
        for (var poll = 0; poll < 5; poll++)
        {
            Assert.True(rounds.TryTake(out var waiting, out _));
            Assert.Equal("a", waiting);
            rounds.Wait("a");
            rounds.Join(["a"]);
        }

        var order = new List<string>();
        for (var i = 0; i < 25 && rounds.TryTake(out var tenantId, out var ticket); i++)
        {
            order.Add(tenantId);
            rounds.Done(tenantId, ticket, foundWork: true);
        }

        var run = 0;
        var longest = 0;
        for (var i = 0; i < order.Count; i++)
        {
            run = i > 0 && order[i] == order[i - 1] ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        Assert.Equal(25, order.Count);
        Assert.Contains("b", order);
        Assert.True(longest <= Quantum, $"A tenant ran {longest} items in a row, more than the quantum {Quantum}.");
    }
}
