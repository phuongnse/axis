using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class CronScheduleTests
{
    private static readonly TimeZoneInfo _berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void A_local_time_that_does_not_exist_runs_at_the_next_valid_time()
    {
        var schedule = Parse("30 2 * * *");

        var first = schedule.NextAfter(Utc("2026-03-28T23:00:00Z"), _berlin);
        var second = schedule.NextAfter(first, _berlin);

        // 02:30 does not exist on 2026-03-29. The clock goes from 02:00 to 03:00, so the run is at 03:00 (01:00Z).
        Assert.Equal(Utc("2026-03-29T01:00:00Z"), first);
        Assert.Equal(Utc("2026-03-30T00:30:00Z"), second);
    }

    [Fact]
    public void A_repeated_local_time_runs_once_at_the_first()
    {
        var schedule = Parse("30 2 * * *");

        var first = schedule.NextAfter(Utc("2026-10-24T22:00:00Z"), _berlin);
        var second = schedule.NextAfter(first, _berlin);

        // 02:30 happens twice on 2026-10-25. Only the first, at 00:30Z, runs.
        Assert.Equal(Utc("2026-10-25T00:30:00Z"), first);
        Assert.Equal(Utc("2026-10-26T01:30:00Z"), second);
    }

    [Fact]
    public void Each_local_time_of_the_repeated_hour_runs_once()
    {
        var schedule = Parse("*/30 * * * *");
        var instant = Utc("2026-10-24T23:45:00Z");

        var occurrences = new List<DateTimeOffset>();
        for (var count = 0; count < 3; count++)
        {
            instant = schedule.NextAfter(instant, _berlin);
            occurrences.Add(instant);
        }

        Assert.Equal([Utc("2026-10-25T00:00:00Z"), Utc("2026-10-25T00:30:00Z"), Utc("2026-10-25T02:00:00Z")], occurrences);
    }

    [Fact]
    public void An_instant_inside_the_second_pass_of_the_repeated_hour_does_not_run_it_again()
    {
        var schedule = Parse("*/30 * * * *");

        // 01:10Z is 02:10 CET, the second pass. The next run is 03:00 CET.
        Assert.Equal(Utc("2026-10-25T02:00:00Z"), schedule.NextAfter(Utc("2026-10-25T01:10:00Z"), _berlin));
    }

    [Fact]
    public void A_schedule_in_utc_runs_after_the_instant_and_never_at_it()
    {
        var schedule = Parse("15 6 * * *");

        Assert.Equal(Utc("2026-06-01T06:15:00Z"), schedule.NextAfter(Utc("2026-06-01T06:14:59Z"), TimeZoneInfo.Utc));
        Assert.Equal(Utc("2026-06-02T06:15:00Z"), schedule.NextAfter(Utc("2026-06-01T06:15:00Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void An_instant_with_another_offset_is_read_in_the_time_zone()
    {
        var schedule = Parse("0 9 * * *");

        // 09:00 in Berlin in summer is 07:00Z.
        var instant = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(5));
        Assert.Equal(Utc("2026-06-02T07:00:00Z"), schedule.NextAfter(instant, _berlin));
    }

    [Fact]
    public void A_day_matches_when_either_the_day_of_month_or_the_day_of_week_matches()
    {
        var schedule = Parse("0 0 13 * 5");
        var instant = Utc("2026-02-01T00:00:00Z");

        var days = new List<DateTimeOffset>();
        for (var count = 0; count < 4; count++)
        {
            instant = schedule.NextAfter(instant, TimeZoneInfo.Utc);
            days.Add(instant);
        }

        // Fridays are 6 and 13 February (13 is both), then Friday 20 February and Friday 27 February.
        Assert.Equal([Utc("2026-02-06T00:00:00Z"), Utc("2026-02-13T00:00:00Z"), Utc("2026-02-20T00:00:00Z"), Utc("2026-02-27T00:00:00Z")], days);
        Assert.Equal(Utc("2026-03-13T00:00:00Z"), Parse("0 0 13 * 5").NextAfter(Utc("2026-03-12T00:00:00Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_field_that_starts_with_a_star_is_unrestricted_for_the_day_rule()
    {
        // Day of month is */2 (odd days) and day of week is 1 (Monday). Both must match.
        var schedule = Parse("0 0 */2 * 1");

        // 2026-06-01 is a Monday and an odd day.
        Assert.Equal(Utc("2026-06-01T00:00:00Z"), schedule.NextAfter(Utc("2026-05-31T00:00:00Z"), TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("0 0 * * 0")]
    [InlineData("0 0 * * 7")]
    public void Sunday_is_0_or_7(string text)
    {
        // 2026-06-07 is a Sunday.
        Assert.Equal(Utc("2026-06-07T00:00:00Z"), Parse(text).NextAfter(Utc("2026-06-01T00:00:00Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_step_over_a_range_and_a_list_are_read()
    {
        var schedule = Parse("10-20/5,45 8 * * *");
        var instant = Utc("2026-06-01T07:00:00Z");

        var minutes = new List<int>();
        for (var count = 0; count < 5; count++)
        {
            instant = schedule.NextAfter(instant, TimeZoneInfo.Utc);
            minutes.Add(instant.Minute);
        }

        Assert.Equal([10, 15, 20, 45, 10], minutes);
    }

    [Fact]
    public void A_leap_day_schedule_finds_the_next_leap_year()
    {
        Assert.Equal(Utc("2028-02-29T00:00:00Z"), Parse("0 0 29 2 *").NextAfter(Utc("2026-06-01T00:00:00Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_expression_is_kept_with_single_spaces()
    {
        Assert.Equal("0 6 * * 1-5", Parse("  0   6 *\t* 1-5 ").Expression);
    }

    [Theory]
    [InlineData("", "0 fields")]
    [InlineData("* * * *", "4 fields")]
    [InlineData("* * * * * *", "6 fields")]
    [InlineData("0 0 30 2 *", "never matches")]
    [InlineData("5-3 * * * *", "ends before")]
    public void An_invalid_cron_says_why(string text, string expected)
    {
        Assert.False(CronSchedule.TryParse(text, out var schedule, out var error));
        Assert.Null(schedule);
        Assert.Contains(expected, error);
    }

    private static CronSchedule Parse(string text) =>
        CronSchedule.TryParse(text, out var schedule, out var error) ? schedule : throw new InvalidOperationException(error);

    private static DateTimeOffset Utc(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
}
