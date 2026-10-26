using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A grouped data source can group a date or date-time field into day, week, month or year buckets.
/// A row holds the first date of its bucket, and a date-time falls in the bucket of its local day in the
/// application time zone.
/// </summary>
public sealed class DataSourceDateBucketTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Asia/Ho_Chi_Minh is UTC+7: Sat 31 Oct 23:00, Sun 1 Nov 01:00, Sun 1 Nov 23:00 and Mon 2 Nov 01:00 local.
    // In UTC the same values are 31 Oct, 31 Oct, 1 Nov and 1 Nov.
    private static readonly string[] _orderedAt =
    [
        "2026-10-31T16:00:00Z",
        "2026-10-31T18:00:00Z",
        "2026-11-01T16:00:00Z",
        "2026-11-01T18:00:00Z",
    ];

    private static readonly string[] _neededBy = ["2026-01-31", "2026-02-01", "2026-02-28", "2027-01-15"];

    public static TheoryData<DateBucket, string[]> LocalBuckets => new()
    {
        { DateBucket.Day, ["2026-10-31=1", "2026-11-01=2", "2026-11-02=1"] },
        { DateBucket.Week, ["2026-10-26=3", "2026-11-02=1"] },
        { DateBucket.Month, ["2026-10-01=1", "2026-11-01=3"] },
    };

    public static TheoryData<DateBucket, string[]> DateBuckets => new()
    {
        { DateBucket.Month, ["2026-01-01=1", "2026-02-01=2", "2027-01-01=1"] },
        { DateBucket.Year, ["2026-01-01=3", "2027-01-01=1"] },
    };

    [Theory]
    [MemberData(nameof(LocalBuckets))]
    public async Task A_date_time_falls_in_the_bucket_of_its_local_day(DateBucket bucket, string[] expected)
    {
        await SeedAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var (dataSource, _) = DataSource("orderedAt", bucket);

        var page = await ListAsync(dataSource, zone, sort: null);

        Assert.Equal(expected, Buckets(page));
        Assert.Equal(expected.Length, page.TotalCount);
    }

    [Fact]
    public async Task The_same_date_times_fall_in_their_utc_buckets_in_a_utc_application()
    {
        await SeedAsync();
        var (dataSource, _) = DataSource("orderedAt", DateBucket.Day);

        var page = await ListAsync(dataSource, TimeZoneInfo.Utc, sort: null);

        Assert.Equal(["2026-10-31=2", "2026-11-01=2"], Buckets(page));
    }

    [Theory]
    [MemberData(nameof(DateBuckets))]
    public async Task A_date_falls_in_the_bucket_that_holds_it(DateBucket bucket, string[] expected)
    {
        await SeedAsync();
        var (dataSource, _) = DataSource("neededBy", bucket);

        var page = await ListAsync(dataSource, TimeZoneInfo.Utc, sort: null);

        Assert.Equal(expected, Buckets(page));
    }

    [Fact]
    public async Task A_sort_by_a_bucketed_group_field_orders_by_the_bucket()
    {
        await SeedAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var (dataSource, _) = DataSource("orderedAt", DateBucket.Month);
        Assert.True(DataSourceSort.TryParse("-orderedAt", dataSource, out var sort));

        var page = await ListAsync(dataSource, zone, sort);

        Assert.Equal(["2026-11-01=3", "2026-10-01=1"], Buckets(page));
    }

    [Fact]
    public async Task A_measure_over_the_bucketed_field_reads_the_raw_values()
    {
        await SeedAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var (dataSource, field) = DataSource("orderedAt", DateBucket.Month);
        var withLatest = dataSource with
        {
            Aggregate = new(
                dataSource.Aggregate!.GroupBy,
                [new("items", AggregateFunction.Count, null), new("latest", AggregateFunction.Max, new DataSourceFieldModel("orderedAt", field))]),
        };

        var page = await ListAsync(withLatest, zone, sort: null);

        Assert.Equal(
            ["2026-10-01", "2026-11-01"],
            page.Items.Select(row => row.Values["orderedAt"]!.GetValue<string>()));
        Assert.Equal(
            ["2026-10-31T16:00:00.000000Z", "2026-11-01T18:00:00.000000Z"],
            page.Items.Select(row => row.Values["latest"]!.GetValue<string>()));
    }

    private (DataSourceModel DataSource, FieldModel Field) DataSource(string fieldName, DateBucket bucket)
    {
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        Assert.True(item.TryGetField(fieldName, out var field));
        var dataSource = new DataSourceModel
        {
            Id = Guid.NewGuid(),
            Name = "Bucketed",
            File = "data-sources/bucketed.json",
            Entity = new EntityReference(item.Id, item.Name),
            Fields = [new DataSourceFieldModel(fieldName, field)],
            Aggregate = new(
                [new DataSourceFieldModel(fieldName, field) { Bucket = bucket }],
                [new("items", AggregateFunction.Count, null)]),
        };
        return (dataSource, field);
    }

    private async Task<DataSourceRowPage> ListAsync(DataSourceModel dataSource, TimeZoneInfo zone, DataSourceSort? sort)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var page = await DataSourceQueries.ListAsync(
            connection, null, fixture.Model with { TimeZone = zone }, dataSource, 1, 100, sort, [], cancellationToken: CancellationToken);
        Assert.NotNull(page);
        return page;
    }

    private static List<string> Buckets(DataSourceRowPage page) =>
    [
        .. page.Items.Select(row => $"{row.Values.First().Value!.GetValue<string>()}={row.Values["items"]!.GetValue<long>()}"),
    ];

    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        for (var index = 0; index < _orderedAt.Length; index++)
        {
            await fixture.InsertAsync(
                TenantA,
                "Item",
                new Dictionary<string, string?> { ["name"] = $"Item {index}", ["orderedAt"] = _orderedAt[index], ["neededBy"] = _neededBy[index] });
        }
    }
}
