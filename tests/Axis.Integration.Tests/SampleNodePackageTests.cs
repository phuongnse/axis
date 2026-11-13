using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.PackageHost;
using Axis.PackageHost.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// What the sample Node.js package <c>payment-terms</c> does when a real Node.js runner of <see cref="TestPackageHost"/> runs
/// it, see docs/reference/extension-packages.md#sample-and-test-packages. The package is installed with <see cref="NodeInstall"/>, so
/// the runner loads <c>dayjs</c> from the version folder. The tests need Node.js and npm on the path, so they are skipped on
/// Windows. Each wait ends on a received line under a deadline, never on elapsed time.
/// </summary>
public sealed class SampleNodePackageTests
{
    private const string PackageId = "payment-terms";

    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(90);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_runner_answers_ready_and_then_the_due_date_at_the_end_of_the_month_30_days_later()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Node.js tests need a Unix shell and the Node.js runtime.");
        using var folder = new TemporaryFolder();
        var install = await NodeInstall.InstallAsync(
            Path.Combine(AppContext.BaseDirectory, "SamplePaymentTerms"),
            Path.Combine(folder.Path, PackageId, "1.0.0"),
            CancellationToken);
        Assert.Null(install.Failure);

        await using var host = await TestPackageHost.StartAsync(folder.Path);
        using var client = new TcpClient();
        await client.ConnectAsync(host.Endpoint, CancellationToken);
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        await writer.WriteLineAsync(RunnerProtocol.Write(new StartMessage(PackageId, "1.0.0", TestPackageHost.ClosedAddress(), "proxy-token")));
        Assert.Equal(new ReadyMessage(), await ReceiveAsync(reader));

        await writer.WriteLineAsync(RunnerProtocol.Write(
            new RunMessage("dueDate", Guid.NewGuid(), new JsonObject { ["from"] = "2026-01-31T10:00:00Z" }, new RunBindings([], []))));
        var result = Assert.IsType<ResultMessage>(await ReceiveAsync(reader));
        Assert.Equal("2026-03-31", result.Output["dueDate"]!.GetValue<string>());
    }

    private static async Task<RunnerMessage> ReceiveAsync(StreamReader reader)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        deadline.CancelAfter(_deadline);
        var line = await reader.ReadLineAsync(deadline.Token) ?? throw new InvalidOperationException("The host closed the connection before the answer.");
        return RunnerProtocol.Read(line);
    }
}
