using System.Text;
using Axis.Data.Naming;
using Axis.Processes.Work;
using static Axis.Integration.Tests.ProcessStepFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A connector output mapped to a sensitive field. It has its own fixture, because the release that adds the
/// field leaves its <c>bytea</c> column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ConnectorCallSensitiveOutputTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private const string Output = "PO-1";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_output_mapped_to_a_sensitive_field_is_stored_as_bytes_without_the_value()
    {
        var tenant = fixture.Tenant("a");
        var request = await File.ReadAllTextAsync(Path.Combine(StepAppFolder, "entities", "request.json"), CancellationToken);
        var order = await File.ReadAllTextAsync(Path.Combine(StepAppFolder, "processes", "order.json"), CancellationToken);
        var (release, model) = await tenant.ActivateCopyWithFilesAsync(
            (
                Path.Combine("entities", "request.json"),
                request.Replace("\"fields\": [", "\"fields\": [\n    { \"name\": \"secret\", \"type\": \"text\", \"sensitive\": true },", StringComparison.Ordinal)),
            (
                Path.Combine("processes", "order.json"),
                order.Replace("\"output\": { \"orderNumber\": \"comment\" }", "\"output\": { \"orderNumber\": \"secret\" }", StringComparison.Ordinal)));
        Assert.True(model.TryGetEntity("Request", out var entity));
        var subject = await tenant.InsertRequestAsync(500m, 1);
        await tenant.StartAsync("Order", subject, release, model);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var stored = await tenant.ScalarAsync(
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("secret"))} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            ("id", subject));
        var bytes = Assert.IsType<byte[]>(stored);
        Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Output)) >= 0);
    }
}
