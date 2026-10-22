using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The process start endpoint and the start lock. These tests activate a release with a pending
/// migration, so they have their own fixture: later activations in a shared one would not match the
/// applied migration history.
/// </summary>
public sealed class ProcessStartLockTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_that_waits_for_an_activation_with_a_pending_migration_pins_the_new_release()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var firstRelease = await fixture.ActiveReleaseIdAsync();
        using var client = fixture.CreateClient().SignedInAs("anna");

        // The activation takes the start lock exclusively and stops before it commits.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activating = fixture.ActivateMigratingReleaseAsync(store => new PausingStore(store, reached, release));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        using var request = Request(HttpMethod.Post, "/api/apps/ProcessApp/processes/Submit/instances", ProcessStartFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        var starting = client.SendAsync(request, CancellationToken);
        await WaitUntilAsync(async () => starting.IsCompleted || await fixture.CountWaitingAdvisoryLocksAsync() > 0, "The start did not finish or wait on a lock.");

        // The start has not ended and the old release is still active, so the start is waiting.
        Assert.False(starting.IsCompleted);
        Assert.Equal(firstRelease, await fixture.ActiveReleaseIdAsync());

        release.SetResult();
        var newRelease = await activating.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);
        using var response = await starting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.NotEqual(firstRelease, newRelease);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(newRelease, body.RootElement.GetProperty("releaseId").GetGuid());
        var instance = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE id = @id AND subject_id = @subject AND release_id = @release",
            ("id", instance), ("subject", subject), ("release", newRelease)));
    }
}
