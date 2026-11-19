using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The next digest time follows the daylight saving rule of schedule triggers, see
/// <c>docs/reference/notifications.md#notification-preferences</c> (Digest). New York changes its clock on 2026-11-01 and 2027-03-14.
/// </summary>
public sealed class EmailDigestTimeTests
{
    private const string Zone = "America/New_York";

    [Theory]
    [InlineData("2026-11-01T04:30:00Z", "2026-11-01T05:00:00Z")]
    [InlineData("2026-11-01T05:00:00Z", "2026-11-02T06:00:00Z")]
    public void Hour_that_happens_twice_gives_one_digest_at_its_first_instant(string now, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), EmailDigests.NextAt(DateTimeOffset.Parse(now), 1, Zone));

    [Theory]
    [InlineData("2027-03-14T06:00:00Z", "2027-03-14T07:00:00Z")]
    [InlineData("2027-03-14T07:00:00Z", "2027-03-15T06:00:00Z")]
    public void Hour_that_does_not_exist_gives_one_digest_at_the_next_valid_local_time(string now, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), EmailDigests.NextAt(DateTimeOffset.Parse(now), 2, Zone));

    [Fact]
    public void Unknown_zone_counts_as_UTC() =>
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T08:00:00Z"), EmailDigests.NextAt(DateTimeOffset.Parse("2026-11-19T07:00:00Z"), 8, "Mars/Base"));
}
