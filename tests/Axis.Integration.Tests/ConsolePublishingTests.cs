using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ConsolePublishingTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publishing_is_refused_without_a_session()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Path(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_is_refused_to_a_user_without_the_operator_role()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("requester");

        using var response = await SendAsync(client, Path(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_answers_the_operator_the_history_and_requests_of_two_applications()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("rita");
        var applications = (await ActiveReleasesAsync()).Take(2).ToList();
        Assert.Equal(2, applications.Count);

        foreach (var application in applications)
        {
            var inserted = new List<Guid>();
            for (var index = 0; index < 2; index++)
            {
                inserted.Add(await InsertRequestAsync(application.ApplicationId));
            }

            using var response = await SendAsync(client, Path(application.ApplicationId));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var root = body.RootElement;
            var expected = await ActivationsAsync(application.ApplicationId);
            Assert.NotEmpty(expected);
            Assert.Equal(application.ReleaseId, root.GetProperty("active").GetProperty("releaseId").GetGuid());
            Assert.Equal(
                expected.Select(row => (row.ReleaseId, row.Number, row.Actor, row.Label, row.Note)),
                root.GetProperty("releases").EnumerateArray().Select(row => (
                    row.GetProperty("releaseId").GetGuid(),
                    row.GetProperty("number").GetInt32(),
                    row.GetProperty("actor").GetString()!,
                    row.GetProperty("label").GetString(),
                    row.GetProperty("note").GetString())));
            Assert.Equal(inserted.AsEnumerable().Reverse(), root.GetProperty("requests").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
            Assert.Equal(2, root.GetProperty("totalCount").GetInt64());
            Assert.Equal(1, root.GetProperty("page").GetInt32());
            var request = root.GetProperty("requests")[0];
            Assert.Equal("publish", request.GetProperty("kind").GetString());
            Assert.Equal("tenant", request.GetProperty("target").GetString());
            Assert.Equal("succeeded", request.GetProperty("status").GetString());
            Assert.Equal("author", request.GetProperty("requestedBy").GetString());
            Assert.Equal("Author User", request.GetProperty("requestedByName").GetString());
            foreach (var hidden in new[] { "snapshot", "diagnostics", "scenarioNames", "scenarioResults", "scenarios" })
            {
                Assert.False(request.TryGetProperty(hidden, out _), hidden);
            }
        }
    }

    [Fact]
    public async Task Publishing_pages_the_requests_newest_first()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("rita");
        var application = Guid.NewGuid();
        var inserted = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            inserted.Add(await InsertRequestAsync(application));
        }

        using var response = await SendAsync(client, Path(application) + "?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(2, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal([inserted[0]], body.RootElement.GetProperty("requests").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("active").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("releases").EnumerateArray());
    }

    [Fact]
    public async Task Publishing_answers_empty_lists_for_an_application_nothing_names()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, Path(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Empty(body.RootElement.GetProperty("releases").EnumerateArray());
        Assert.Empty(body.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Publishing_answers_404_for_an_id_that_is_not_a_hyphenated_uuid()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, "/api/console/applications/not-an-id/publishing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_answers_400_for_an_invalid_page()
    {
        await using var factory = OperatorOnly();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, Path(Guid.NewGuid()) + "?page=0&pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The operator role is reader, and neither the author nor the publisher role of Studio is held by anyone the test signs in as.
    private WebApplicationFactory<Program> OperatorOnly() =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Console:OperatorRole", "reader");
            builder.UseSetting("Studio:AuthorRole", "studio-author");
            builder.UseSetting("Studio:PublisherRole", "studio-publisher");
        });

    private static string Path(Guid application) => $"/api/console/applications/{application:D}/publishing";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = Request(path, HostA);
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<IReadOnlyList<ActiveRelease>> ActiveReleasesAsync()
    {
        await using var configuration = Configuration();
        return await new ActiveReleaseStore(configuration).ListAsync(CancellationToken);
    }

    private async Task<IReadOnlyList<ReleaseActivation>> ActivationsAsync(Guid application)
    {
        await using var configuration = Configuration();
        return await new ActiveReleaseStore(configuration).ListActivationsAsync(application, CancellationToken);
    }

    private ConfigurationDbContext Configuration() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(fixture.ConnectionString(TenantA)).Options);

    // A request of an application only this test names, or of an active application, which the test reads back by id.
    private async Task<Guid> InsertRequestAsync(Guid application)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, requested_by, requested_by_name, requested_at, finished_at)
            VALUES (@id, @application, 'publish', 'succeeded', 'author', 'Author User', clock_timestamp(), clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", application);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }
}
