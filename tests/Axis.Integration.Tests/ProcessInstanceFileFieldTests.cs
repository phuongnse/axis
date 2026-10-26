using System.Net;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A <c>sensitive</c> file field in process inspection and step history. It has its own fixture, because the release
/// that adds the field leaves its column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ProcessInstanceFileFieldTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sensitive_file_field_leaves_the_id_and_the_name_of_its_file_out_of_the_detail_and_the_step_history()
    {
        var release = await fixture.ActivateEditedAsync(
            TenantB,
            (path, content) => path == Path.Combine("entities", "request.json")
                ? content.Replace("\"fields\": [", "\"fields\": [\n    { \"name\": \"scan\", \"type\": \"file\", \"sensitive\": true },", StringComparison.Ordinal)
                : content);
        Assert.True(fixture.Model.TryGetEntity("Request", out var entity));
        var subject = await fixture.InsertRequestAsync(TenantB, null);
        var file = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            TenantB,
            """
            INSERT INTO axis.files (id, application_id, entity_id, field_path, owner_record_id, name, media_type, size, sha256, storage_kind, state, uploaded_by, uploaded_at)
            VALUES (@id, @application, @entity, 'scan', @record, 'secret-scan.pdf', 'application/pdf', 9, @sha256, 'database', 'attached', 'ada', now())
            """,
            ("id", file),
            ("application", fixture.Model.Manifest.Id),
            ("entity", entity.Id),
            ("record", subject),
            ("sha256", new byte[32]));
        await fixture.ExecuteAsync(
            TenantB,
            $"UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} SET {EntityNaming.Quote(EntityNaming.Column("scan"))} = @id WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @record",
            ("id", file),
            ("record", subject));
        var instance = await fixture.StartAsync(TenantB, "DepartmentOrder", subject, release);
        Assert.True(await fixture.RunNextStepAsync(TenantB, CancellationToken));
        await fixture.InsertConnectorCallAsync(TenantB, instance, "createOrder", """{"reference":"Sales","amount":500}""");

        using var client = fixture.CreateClient().SignedInAs("ada");
        using var message = RecordApiFixture.Request($"/api/apps/StepApp/processes/DepartmentOrder/instances/{instance:D}", Host(TenantB));
        using var response = await client.SendAsync(message, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadAsStringAsync(CancellationToken);
        var history = await fixture.HistoryAsync(TenantB, instance);
        Assert.NotEmpty(history);
        foreach (var text in new[] { detail }.Concat(history.SelectMany(row => new[] { row.Input, row.Output, row.Error })).OfType<string>())
        {
            Assert.DoesNotContain(file.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-scan", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
