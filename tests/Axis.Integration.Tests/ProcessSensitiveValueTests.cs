using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A process step that writes or reads a sensitive field. It has its own fixture, because the release that adds the
/// field leaves its <c>bytea</c> column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ProcessSensitiveValueTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Sealed = "Sealed by the step";

    private const string Records = "/api/apps/StepApp/entities/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_step_that_sets_a_sensitive_field_stores_bytes_without_the_value_and_the_record_api_returns_it()
    {
        var (release, _) = await ActivateAsync();
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Submit", subject, release);

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await fixture.SnapshotAsync(TenantA, instance)).State);
        var stored = await StoredAsync(subject);
        Assert.False(stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Sealed)) >= 0);
        using var client = fixture.CreateClient().SignedInAs("maria");
        Assert.Equal(Sealed, await SecretAsync(client, subject));
    }

    [Fact]
    public async Task Process_inspection_of_an_instance_that_set_a_sensitive_field_does_not_show_the_value()
    {
        var (release, _) = await ActivateAsync();
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Submit", subject, release);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        Assert.Equal(ProcessStarts.Completed, (await fixture.SnapshotAsync(TenantA, instance)).State);

        using var client = fixture.CreateClient().SignedInAs("ines");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/Submit/instances/{instance:D}", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Sealed, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_reads_a_sensitive_value_that_the_record_api_stored()
    {
        var (release, model) = await ActivateAsync();
        using var client = fixture.CreateClient().SignedInAs("maria");
        using var created = await client.SendAsync(
            RecordApiFixture.Request(HttpMethod.Post, Records, Host(TenantA), """{ "values": { "title": "Reseal me", "secret": "Stored by the API" } }"""),
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var subject = body.RootElement.GetProperty("id").GetGuid();
        var instance = await fixture.StartAsync(TenantA, "Reseal", subject, release, model);

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await fixture.SnapshotAsync(TenantA, instance)).State);
        Assert.All(await fixture.HistoryAsync(TenantA, instance), row => Assert.Null(row.Error));
        Assert.Equal("Stored by the API and seen", await SecretAsync(client, subject));
    }

    // Activates StepApp with the text field "secret", sensitive from the release that adds it, set by the Submit process,
    // and with the Reseal process, which appends to the field.
    private Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync() =>
        fixture.ActivateEditedWithModelAsync(
            TenantA,
            (path, content) =>
            {
                if (path == Path.Combine("entities", "request.json"))
                {
                    return content.Replace(
                        "\"fields\": [",
                        "\"fields\": [\n    { \"name\": \"secret\", \"type\": \"text\", \"sensitive\": true },",
                        StringComparison.Ordinal);
                }

                if (path == Path.Combine("processes", "submit.json"))
                {
                    return content.Replace(
                        "\"status\": \"'submitted'\",",
                        $"\"status\": \"'submitted'\", \"secret\": \"'{Sealed}'\",",
                        StringComparison.Ordinal);
                }

                return content;
            },
            added: new Dictionary<string, string>
            {
                [Path.Combine("processes", "reseal.json")] = """
                    {
                      "id": "5f0a8d3e-7c21-4b96-a4e8-1d6c92b7f035",
                      "kind": "process",
                      "name": "Reseal",
                      "formatVersion": 1,
                      "entity": "Request",
                      "start": "reseal",
                      "steps": [
                        {
                          "name": "reseal",
                          "type": "operation",
                          "operation": "updateRecord",
                          "set": { "secret": "concat(secret, ' and seen')" },
                          "next": "done"
                        },
                        { "name": "done", "type": "end" }
                      ]
                    }
                    """,
            });

    private async Task<byte[]> StoredAsync(Guid subject)
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var entity));
        var stored = await fixture.ScalarAsync(
            TenantA,
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("secret"))} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            ("id", subject));
        return Assert.IsType<byte[]>(stored);
    }

    private static async Task<string?> SecretAsync(HttpClient client, Guid subject)
    {
        using var response = await client.SendAsync(RecordApiFixture.Request($"{Records}/{subject:D}", Host(TenantA)), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("values").GetProperty("secret").GetString();
    }
}
