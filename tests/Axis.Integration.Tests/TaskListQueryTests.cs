using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class TaskListQueryTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_filter_and_sort_apply_to_the_subject_and_the_due_date_and_the_total_counts_the_matches()
    {
        var marker = Marker();
        var alpha = await CreateTaskAsync("Approve", $"{marker} Alpha", "maria");
        var bravo = await CreateTaskAsync("Approve", $"{marker} Bravo", "maria");
        var charlie = await CreateTaskAsync("RoleApprove", $"{marker} Charlie", "maria");
        using var maria = fixture.CreateClient().SignedInAs("maria");
        var search = $"search={marker}";

        using var descending = await ListAsync(maria, $"{search}&sort=-subject");
        Assert.Equal([charlie, bravo, alpha], Ids(descending));
        Assert.Equal(3, descending.RootElement.GetProperty("totalCount").GetInt64());

        using var ascending = await ListAsync(maria, $"{search}&sort=subject");
        Assert.Equal([alpha, bravo, charlie], Ids(ascending));

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var filter = Filter(("dueAt", "gte", today), ("subject", "contains", "Bravo"));
        using var filtered = await ListAsync(maria, $"{search}&filter={filter}");
        Assert.Equal([bravo], Ids(filtered));
        Assert.Equal(1, filtered.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_search_with_no_sort_keeps_the_due_date_order_with_undated_tasks_last()
    {
        var marker = Marker();
        var undated = await CreateTaskAsync("RoleApprove", $"{marker} Aardvark", "maria");
        var dated = await CreateTaskAsync("Approve", $"{marker} Zebra", "maria");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var list = await ListAsync(maria, $"search={marker}");

        Assert.Equal([dated, undated], Ids(list));
        Assert.Equal(2, list.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData("filter", "step", "eq", "approve")]
    [InlineData("sort", "nope", null, null)]
    public async Task A_field_the_list_does_not_hold_is_400_keyed_by_the_parameter(string key, string field, string? op, string? value)
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");
        var parameter = key == "filter" ? Filter((field, op!, value!)) : field;

        using var response = await SendAsync(maria, $"{Tasks}?{key}={parameter}");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Nobody_signed_in_is_401_with_a_sort()
    {
        using var client = fixture.CreateClient();

        using var response = await SendAsync(client, $"{Tasks}?sort=subject");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_subject_label_the_user_may_not_read_is_neither_searched_nor_filtered_and_its_reader_finds_it()
    {
        var hiddenMarker = Marker();
        var hidden = await CreateTaskAsync("Decide", $"{hiddenMarker} Hidden", "otto");
        var shownMarker = Marker();
        var shown = await CreateTaskAsync("Approve", $"{shownMarker} Shown", "maria");
        using var otto = fixture.CreateClient().SignedInAs("otto");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var ownTask = await ListAsync(otto, "sort=-subject");
        Assert.Contains(hidden, Ids(ownTask));

        using var searched = await ListAsync(otto, $"search={hiddenMarker}");
        Assert.Empty(Ids(searched));
        Assert.Equal(0, searched.RootElement.GetProperty("totalCount").GetInt64());

        using var filtered = await ListAsync(otto, $"filter={Filter(("subject", "contains", hiddenMarker))}");
        Assert.Empty(Ids(filtered));
        Assert.Equal(0, filtered.RootElement.GetProperty("totalCount").GetInt64());

        using var found = await ListAsync(maria, $"search={shownMarker}");
        Assert.Equal([shown], Ids(found));
    }

    // A marker of ten or more digits, so a search finds only this test's tasks among those of the other tests.
    private static string Marker() => Random.Shared.NextInt64(10_000_000_000, 90_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Filter(params (string Field, string Op, string Value)[] conditions) =>
        Uri.EscapeDataString(JsonSerializer.Serialize(new
        {
            all = conditions.Select(condition => new { field = condition.Field, op = condition.Op, value = condition.Value }),
        }));

    /// <summary>
    /// Creates a request titled <paramref name="title"/> in a department managed by <paramref name="manager"/>, starts
    /// <paramref name="process"/> for it and runs the tenant's work items. Returns the task's id.
    /// </summary>
    private async Task<Guid> CreateTaskAsync(string process, string title, string manager)
    {
        var department = await fixture.InsertDepartmentAsync(TenantA, manager);
        var subject = await fixture.InsertRequestAsync(TenantA, department, title: title);
        var instance = await fixture.StartAsync(TenantA, process, subject);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        return await fixture.TaskIdAsync(TenantA, instance);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = RecordApiFixture.Request(path, Host(TenantA));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ListAsync(HttpClient client, string query)
    {
        using var response = await SendAsync(client, $"{Tasks}?pageSize=100&{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> Ids(JsonDocument list) =>
        [.. list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
}
