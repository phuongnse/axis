using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ProcessInspectionEndpointTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Instances = "/api/apps/ProcessApp/processes/Submit/instances";
    private const string JobInstances = "/api/apps/ProcessApp/processes/NightlyJob/instances";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_with_inspect_lists_the_instances_newest_first_with_their_properties()
    {
        var activeRelease = await fixture.ActiveReleaseIdAsync();
        var first = await StartAsync();
        var second = await StartAsync();
        var third = await StartAsync();
        await fixture.SetInstanceStateAsync(first.Id, "completed");

        using var response = await GetAsync($"{Instances}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.Equal(["items", "page", "pageSize", "totalCount"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal((1, 100), (root.GetProperty("page").GetInt32(), root.GetProperty("pageSize").GetInt32()));
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(items.Count, root.GetProperty("totalCount").GetInt64());

        var mine = new[] { first, second, third }.Select(instance => instance.Id).ToHashSet();
        Assert.Equal(
            [third.Id, second.Id, first.Id],
            items.Select(item => item.GetProperty("id").GetGuid()).Where(mine.Contains));
        var createdAt = items.Select(item => item.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(createdAt.OrderDescending(), createdAt);

        var item1 = items.Single(item => item.GetProperty("id").GetGuid() == first.Id);
        Assert.Equal(
            ["id", "subjectId", "state", "step", "releaseId", "createdAt", "endedAt"],
            item1.EnumerateObject().Select(property => property.Name));
        Assert.Equal(first.SubjectId, item1.GetProperty("subjectId").GetGuid());
        Assert.Equal("completed", item1.GetProperty("state").GetString());
        Assert.Equal("done", item1.GetProperty("step").GetString());
        Assert.Equal(activeRelease, item1.GetProperty("releaseId").GetGuid());
        Assert.Equal(DateTimeKind.Utc, item1.GetProperty("createdAt").GetDateTime().Kind);
        Assert.Equal(JsonValueKind.String, item1.GetProperty("endedAt").ValueKind);

        var item2 = items.Single(item => item.GetProperty("id").GetGuid() == second.Id);
        Assert.Equal("running", item2.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, item2.GetProperty("endedAt").ValueKind);
    }

    [Fact]
    public async Task The_state_filter_keeps_only_the_instances_in_that_state()
    {
        var completed = await StartAsync();
        var failed = await StartAsync();
        var running = await StartAsync();
        await fixture.SetInstanceStateAsync(completed.Id, "completed");
        await fixture.SetInstanceStateAsync(failed.Id, "failed");

        var items = await ListAsync("state=completed");
        var ids = items.Select(item => item.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(completed.Id, ids);
        Assert.DoesNotContain(failed.Id, ids);
        Assert.DoesNotContain(running.Id, ids);
        Assert.All(items, item => Assert.Equal("completed", item.GetProperty("state").GetString()));
        Assert.Contains(running.Id, (await ListAsync("state=running")).Select(item => item.GetProperty("id").GetGuid()));
        Assert.Contains(failed.Id, (await ListAsync("state=failed")).Select(item => item.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task The_subject_filter_keeps_only_the_instances_of_that_record()
    {
        var wanted = await StartAsync();
        await StartAsync();

        var items = await ListAsync($"subject={wanted.SubjectId:D}");

        Assert.Equal([wanted.Id], items.Select(item => item.GetProperty("id").GetGuid()));
        using var response = await GetAsync($"{Instances}?subject={wanted.SubjectId:D}&state=completed");
        using var body = await ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_page_has_at_most_the_page_size_and_the_total_counts_every_page()
    {
        await StartAsync();
        await StartAsync();

        using var response = await GetAsync($"{Instances}?page=1&pageSize=1");
        using var body = await ReadJsonAsync(response);
        var total = body.RootElement.GetProperty("totalCount").GetInt64();
        using var past = await GetAsync($"{Instances}?page={total + 1}&pageSize=1");
        using var pastBody = await ReadJsonAsync(past);

        Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(total >= 2);
        Assert.Empty(pastBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(total, pastBody.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData("state=nope", "state")]
    [InlineData("state=Running", "state")]
    [InlineData("state=", "state")]
    [InlineData("subject=abc", "subject")]
    [InlineData("subject=", "subject")]
    [InlineData("subject=00000000000000000000000000000001", "subject")]
    [InlineData("subject={00000000-0000-0000-0000-000000000001}", "subject")]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task An_invalid_parameter_is_a_400_keyed_by_its_name(string query, string name)
    {
        using var response = await GetAsync($"{Instances}?{query}");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([name], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task Every_invalid_parameter_is_reported_in_one_400()
    {
        using var response = await GetAsync($"{Instances}?state=nope&subject=abc&page=0");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(
            ["page", "state", "subject"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task A_user_with_read_access_but_no_inspect_grant_is_a_403()
    {
        // Olga may read every request, but holds no grant on the process.
        using var olga = fixture.CreateClient().SignedInAs("olga");

        using var response = await GetAsync(Instances, olga);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_user_with_inspect_lists_an_instance_of_a_process_without_a_subject_with_a_null_subject()
    {
        var id = await StartJobAsync();

        using var response = await GetAsync($"{JobInstances}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var item = body.RootElement.GetProperty("items").EnumerateArray().Single(candidate => candidate.GetProperty("id").GetGuid() == id);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("subjectId").ValueKind);
        Assert.Equal("running", item.GetProperty("state").GetString());

        using var other = await GetAsync($"{JobInstances}?subject={Guid.NewGuid():D}");
        using var otherBody = await ReadJsonAsync(other);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(0, otherBody.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_user_with_inspect_reads_an_instance_of_a_process_without_a_subject_with_no_commands()
    {
        var id = await StartJobAsync();

        using var response = await GetAsync($"{JobInstances}/{id:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subjectId").ValueKind);
        Assert.Equal("NightlyJob", root.GetProperty("process").GetString());
        Assert.Equal("check", root.GetProperty("graph").GetProperty("start").GetString());
        Assert.Equal(0, root.GetProperty("commands").GetArrayLength());
    }

    [Fact]
    public async Task A_user_without_inspect_on_a_process_without_a_subject_is_a_403_on_the_list_and_the_detail()
    {
        var id = await StartJobAsync();
        using var olga = fixture.CreateClient().SignedInAs("olga");

        foreach (var path in new[] { JobInstances, $"{JobInstances}/{id:D}" })
        {
            using var response = await GetAsync(path, olga);

            using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task A_request_with_no_session_is_a_401()
    {
        using var anonymous = fixture.CreateClient();

        using var response = await GetAsync(Instances, anonymous);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/api/apps/Nope/processes/Submit/instances", "No application is active under this name.")]
    [InlineData("/api/apps/ProcessApp/processes/Nope/instances", "The application has no process with this name.")]
    public async Task An_unknown_application_or_process_is_a_404(string path, string title)
    {
        using var response = await GetAsync(path);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_instance_whose_subject_record_is_outside_the_users_read_filter_is_not_listed_or_counted()
    {
        // Anna reads only the requests whose title starts with "Request".
        var visible = await StartAsync();
        var hiddenSubject = await fixture.InsertRequestAsync(status: null, title: "Hidden");
        var hidden = await fixture.InsertInstanceAsync(hiddenSubject);
        var missing = await fixture.InsertInstanceAsync(Guid.NewGuid());

        using var response = await GetAsync($"{Instances}?pageSize=100");

        using var body = await ReadJsonAsync(response);
        var ids = body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(visible.Id, ids);
        Assert.DoesNotContain(hidden, ids);
        Assert.DoesNotContain(missing, ids);
        Assert.Equal(ids.Count, body.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(ids.Count, await CountReadableInstancesAsync());
        Assert.True(await fixture.CountAsync("SELECT count(*) FROM axis.process_instances") > ids.Count);

        using var named = await GetAsync($"{Instances}?subject={hiddenSubject:D}");
        using var namedBody = await ReadJsonAsync(named);
        Assert.Empty(namedBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, namedBody.RootElement.GetProperty("totalCount").GetInt64());
    }

    private async Task<long> CountReadableInstancesAsync()
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));
        var id = EntityNaming.Quote(EntityNaming.IdColumn);
        var title = EntityNaming.Quote(EntityNaming.Column("title"));
        return await fixture.CountAsync(
            $"""
            SELECT count(*) FROM axis.process_instances i
            WHERE EXISTS (SELECT 1 FROM {table} r WHERE r.{id} = i.subject_id AND starts_with(r.{title}, 'Request'))
            """);
    }

    private async Task<(Guid Id, Guid SubjectId)> StartAsync()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        using var request = Request(HttpMethod.Post, Instances, ProcessStartFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return (body.RootElement.GetProperty("id").GetGuid(), subject);
    }

    private async Task<Guid> StartJobAsync()
    {
        Assert.True(fixture.Model.TryGetProcess("NightlyJob", out var process));
        await fixture.EndInstancesAsync(process.Id);
        using var request = Request(HttpMethod.Post, JobInstances, ProcessStartFixture.Host, """{ "parameters": { "days": 3 } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<List<JsonElement>> ListAsync(string query)
    {
        using var response = await GetAsync($"{Instances}?{query}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
    }

    private async Task<HttpResponseMessage> GetAsync(string path, HttpClient? client = null)
    {
        using var request = Request(path, ProcessStartFixture.Host);
        return await (client ?? fixture.Client).SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
