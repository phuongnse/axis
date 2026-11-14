using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Packages;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The payment reference and payment due date steps of an approved purchase request, run by a worker with
/// <c>Extensions:Isolation</c> <c>process</c>. The worker holds no package assembly. A C# runner and a Node.js runner of a
/// package host in this process run the sample packages <c>payment-references</c> and <c>payment-terms</c> in real
/// processes, see docs/reference/extension-packages.md#sample-and-test-packages. <c>requester</c> starts the approval and
/// <c>manager-2</c> approves it.
/// </summary>
public sealed partial class PackageRunnerPurchaseRequestTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_payment_reference_and_due_date_come_from_runners_while_the_worker_loads_no_package()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.InstalledPackagesPath);
        await using var worker = fixture.BuildWorker(new Dictionary<string, string?>
        {
            [PackageIsolation.IsolationSetting] = "process",
            [PackageIsolation.DotnetRunnerSetting] = host.Address,
            [PackageIsolation.NodeRunnerSetting] = host.Address,
            [PackageIsolation.ProxySetting] = "0",
        });
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;
        using (var approved = await CompleteAsync(manager2, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        // The in-process worker runs the steps and the connector calls, and the worker with runners runs the package calls.
        await fixture.RunKindsUntilIdleAsync(
            CancellationToken, ProcessStarts.StepWorkItemKind, ProcessStarts.ConnectorCallWorkItemKind, ProcessStarts.WebhookDeliverWorkItemKind);
        Assert.Equal("assignPaymentReference", await fixture.ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", ("id", instance)));
        var runner = worker.GetRequiredService<WorkItemRunner>();
        Assert.Equal(1, await fixture.RunKindsUntilIdleAsync(runner, CancellationToken, ProcessStarts.PackageCallWorkItemKind));
        await fixture.RunKindsUntilIdleAsync(CancellationToken, ProcessStarts.StepWorkItemKind);
        Assert.Equal("assignPaymentDueDate", await fixture.ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(1, await fixture.RunKindsUntilIdleAsync(runner, CancellationToken, ProcessStarts.PackageCallWorkItemKind));
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance)));
        var (paymentReference, submittedAt, paymentDueDate) = await RecordAsync(requester, subject);
        Assert.Matches(PaymentReference(), paymentReference ?? string.Empty);

        // 30 days after the submission in UTC, moved to the last day of that month.
        var later = submittedAt.UtcDateTime.AddDays(30);
        Assert.Equal(
            new DateOnly(later.Year, later.Month, DateTime.DaysInMonth(later.Year, later.Month)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            paymentDueDate);
        Assert.Single(host.RunnerIds("payment-references", "1.0.0"));
        Assert.Single(host.RunnerIds("payment-terms", "1.0.0"));
        Assert.Null(worker.GetRequiredService<PackageCatalog>().Find("payment-references", "1.0.0")!.LoadContext);
    }

    private static async Task<(string? PaymentReference, DateTimeOffset SubmittedAt, string? PaymentDueDate)> RecordAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"/api/apps/PurchaseRequests/entities/PurchaseRequest/records/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        return (
            values.TryGetProperty("paymentReference", out var reference) ? reference.GetString() : null,
            values.GetProperty("submittedAt").GetDateTimeOffset(),
            values.TryGetProperty("paymentDueDate", out var dueDate) ? dueDate.GetString() : null);
    }

    [GeneratedRegex(@"^RF\d{2}")]
    private static partial Regex PaymentReference();
}
