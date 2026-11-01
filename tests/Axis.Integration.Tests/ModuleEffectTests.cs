using Axis.Data.Records;

namespace Axis.Integration.Tests;

/// <summary>
/// The effects of the <c>ledgers</c> module's <c>Claim</c> contract run in every record update that makes their
/// transition. Approving a submitted claim creates one <c>Entry</c> in the same transaction, with the accountable user
/// as its creator, and the written result lists it with the point <c>Claim/effects/entryRecorded</c>. An update that
/// keeps the state, or makes the <c>submit</c> transition, creates none. When the entry fails its own validation, the
/// update is refused with the effect's point, and after the rollback the claim and the entries are unchanged.
/// </summary>
public sealed class ModuleEffectTests(ModuleEffectFixture fixture) : IClassFixture<ModuleEffectFixture>
{
    private const string Point = "Claim/effects/entryRecorded";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private ProcessStepFixture Steps => fixture.Steps;

    [Fact]
    public async Task An_update_that_makes_the_transition_creates_its_effect_record_for_the_accountable_user()
    {
        var id = await fixture.InsertClaimAsync("submitted", 40);

        var (result, version) = await UpdateAsync(id, 1, new RecordWriteContext("approver-1", null), "approved", commit: true);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        var effect = Assert.Single(result.Effects);
        Assert.Equal(Point, effect.Point);
        Assert.Equal("ledgers/Entry", effect.Entity.Name);
        Assert.Equal(id.ToString("D"), effect.Record.Values["claim"]!.GetValue<string>());
        Assert.Equal(("approved", version), await fixture.ClaimAsync(id));
        Assert.Equal([((long?)40, "approver-1", "open")], await fixture.EntriesAsync(id));
    }

    [Fact]
    public async Task An_update_without_an_accountable_user_leaves_the_creator_of_the_effect_record_null()
    {
        var id = await fixture.InsertClaimAsync("submitted", 40);

        var (result, _) = await UpdateAsync(id, 1, RecordWriteContext.None, "approved", commit: true);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Equal([((long?)40, (string?)null, "open")], await fixture.EntriesAsync(id));
    }

    [Fact]
    public async Task An_update_that_keeps_the_state_creates_no_effect_record()
    {
        var id = await fixture.InsertClaimAsync("submitted", 40);
        Assert.True(fixture.ClaimEntity.TryGetField("amount", out var amount));

        await using var connection = await Steps.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.UpdateAsync(connection, Steps.Model, fixture.ClaimEntity, Steps.FirstReleaseId, new RecordWriteContext("approver-1", null), id, 1, [new RecordValue(amount, 50L)], [], cancellationToken: CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Empty(result.Effects);
        Assert.Empty(await fixture.EntriesAsync(id));
    }

    [Fact]
    public async Task An_update_that_makes_another_transition_creates_no_effect_record()
    {
        var id = await fixture.InsertClaimAsync("draft", 40);

        var (result, version) = await UpdateAsync(id, 1, new RecordWriteContext("clerk-1", null), "submitted", commit: true);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Empty(result.Effects);
        Assert.Equal(("submitted", version), await fixture.ClaimAsync(id));
        Assert.Empty(await fixture.EntriesAsync(id));
    }

    [Fact]
    public async Task An_effect_record_that_fails_its_validation_refuses_the_update_and_the_rollback_leaves_nothing()
    {
        var id = await fixture.InsertClaimAsync("submitted", -1);

        var (result, _) = await UpdateAsync(id, 1, new RecordWriteContext("approver-1", null), "approved", commit: false);

        Assert.Equal(RecordWriteOutcome.EffectRefused, result.Outcome);
        Assert.Equal(Point, result.Point);
        Assert.Null(result.Errors);
        Assert.Empty(result.Effects);
        Assert.Equal(("submitted", 1L), await fixture.ClaimAsync(id));
        Assert.Empty(await fixture.EntriesAsync(id));
    }

    /// <summary>Sets the claim's status in a transaction, which it commits or rolls back, and returns the result and the version it ends at.</summary>
    private async Task<(RecordWriteResult Result, long Version)> UpdateAsync(Guid id, long version, RecordWriteContext context, string status, bool commit)
    {
        Assert.True(fixture.ClaimEntity.TryGetField("status", out var field));
        await using var connection = await Steps.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.UpdateAsync(connection, Steps.Model, fixture.ClaimEntity, Steps.FirstReleaseId, context, id, version, [new RecordValue(field, status)], [], cancellationToken: CancellationToken);
        if (commit)
        {
            await transaction.CommitAsync(CancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(CancellationToken);
        }

        return (result, result.Record?.Version ?? version);
    }
}
