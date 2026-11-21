using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Processes.Instances;
using Axis.Tenancy;

namespace Axis.Admin;

/// <summary>
/// Runs one operation of one package of a snapshot for a <c>run</c> request, see docs/reference/package-runners.md#an-applications-packages. It
/// builds the package in <c>application-package-host</c> as <see cref="ApplicationPackageBuilds.BuildPackageAsync"/> does, starts one runner from the
/// built files with the tenant's id, and sends one <c>run</c> that holds the input and the tenant's <c>url</c> and <c>secret</c> bindings that the
/// operation declares. The runner goes through the publisher's own <see cref="EgressProxy"/>, which admits only the hosts of those <c>url</c>
/// bindings while the call runs, as the worker's does. The runner is stopped after each run and none is kept idle, so no tenant code keeps running in
/// the publisher's name. The call is bounded by <c>wait</c>, as the build is. No log line and no message holds the input, a secret or the output.
/// </summary>
internal sealed class ApplicationPackageRuns(ApplicationPackageBuilds builds, PackageRunners pool, EgressProxy proxy, TimeSpan wait) : IAsyncDisposable
{
    /// <summary>The code of a run whose operation the built package does not declare.</summary>
    public const string OperationCode = "operation";

    /// <summary>The code of a run whose input is not an object that fits the operation's inputs.</summary>
    public const string InputCode = "input";

    /// <summary>The code of a run that needs a binding the tenant lacks, or that asked for one it does not declare.</summary>
    public const string BindingCode = "binding";

    /// <summary>The code of a run that did not finish in time.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>The code of a run whose host or runner could not be used, or closed its connection.</summary>
    public const string ExitedCode = "exited";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    /// <summary>
    /// Builds the package <paramref name="package"/> of <paramref name="files"/>, starts a runner from the built files and calls
    /// <paramref name="operation"/> with <paramref name="input"/>, a JSON object. A refused or unanswered build returns its diagnostics and the lines
    /// the host wrote, and no run. Otherwise the run holds <c>operation</c>, <c>outcome</c>, <c>output</c>, <c>error</c> and <c>durationMs</c>, and its
    /// error code is the error's type name, <see cref="OperationCode"/>, <see cref="InputCode"/>, <see cref="BindingCode"/>, <see cref="TimeoutCode"/> or
    /// <see cref="ExitedCode"/>, because the protocol carries no exception message.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PackageRunResult> RunAsync(
        IReadOnlyList<ResourceContent> files,
        string package,
        string operation,
        string? input,
        string tenantId,
        IDeploymentBindings bindings,
        bool allowHttp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(bindings);

        var built = await builds.BuildPackageAsync(files, package, cancellationToken);
        if (built.Diagnostics.Count > 0)
        {
            return new PackageRunResult(built.Diagnostics, built.Output, null);
        }

        PackageManifest? manifest = built.Files.TryGetValue(PackageCatalog.ManifestFileName, out var text)
            ? PackageCatalog.ReadManifestText(Encoding.UTF8.GetString(text))
            : null;
        if (manifest is not { CustomerHosted: false } || manifest.Operations.FirstOrDefault(declared => declared.Name == operation) is not { } declaration)
        {
            return Ended(built, operation, OperationCode, $"The package '{package}' declares no operation '{operation}'.");
        }

        if (ReadValues(input, declaration) is not { } values)
        {
            return Ended(built, operation, InputCode, $"The input does not fit the inputs of the operation '{operation}'.");
        }

        if (!PackageBindings.TryResolve(declaration.Capabilities, tenantId, bindings, allowHttp, out var urls, out var secrets, out var bindingError))
        {
            return Ended(built, operation, BindingCode, bindingError);
        }

        JsonObject encoded;
        try
        {
            encoded = RunnerValues.Encode(declaration.Input, values);
        }
        catch (InvalidCastException)
        {
            return Ended(built, operation, InputCode, $"The input does not fit the inputs of the operation '{operation}'.");
        }

        RunnerConnection runner;
        using (var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            starting.CancelAfter(PackageRunners.StartTimeout);
            try
            {
                runner = await pool.RentAsync(manifest.Id, manifest.Version, starting.Token, tenantId, built.Files);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A host that cannot be reached, or starts no runner, and a start that takes too long.
                return Ended(built, operation, ExitedCode, PackageCallHandler.RunnerUnreachable(pool.Setting));
            }
        }

        try
        {
            var (run, cpuSeconds) = await CallAsync(runner, declaration, package, encoded, urls, secrets, cancellationToken);
            return new PackageRunResult([], built.Output, run, cpuSeconds);
        }
        finally
        {
            // Closing the connection makes the host kill the runner and delete its copy of the files.
            await runner.StopAsync();
            runner.Dispose();
        }
    }

    /// <summary>Stops the pool, then the proxy.</summary>
    public async ValueTask DisposeAsync()
    {
        await pool.DisposeAsync();
        await proxy.DisposeAsync();
    }

    // Sends the one run. The runner's proxy admits the hosts of the declared url bindings for this call only. A request that it refused ends the run
    // whatever the runner answered, and so does a timeout. The call's CPU seconds are counted as the worker counts a call, see
    // docs/reference/tenant-quotas.md#package-run-time-a-day, and go back beside the run, never into it.
    private async Task<(JsonObject Run, long CpuSeconds)> CallAsync(
        RunnerConnection runner,
        PackageOperation declaration,
        string package,
        JsonObject encoded,
        Dictionary<string, Uri> urls,
        Dictionary<string, Func<string>> secrets,
        CancellationToken cancellationToken)
    {
        var message = new RunMessage(
            declaration.Name,
            Guid.NewGuid(),
            encoded,
            new RunBindings(
                urls.ToDictionary(pair => pair.Key, pair => pair.Value.AbsoluteUri),
                secrets.ToDictionary(pair => pair.Key, pair => pair.Value())));
        runner.Admit(urls.Values);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        RunnerReply reply = default;
        var timedOut = false;
        var sent = Stopwatch.GetTimestamp();
        try
        {
            reply = await runner.RunAsync(message, timeout.Token);
        }
        catch (Exception)
        {
            // The wait or the stop. A stop is not caught.
            if (cancellationToken.IsCancellationRequested)
            {
                runner.EndCall();
                throw;
            }

            timedOut = true;
        }

        var duration = (long)Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
        var cpuSeconds = PackageCallHandler.RunTimeSeconds(reply.CpuMilliseconds, sent);
        if (runner.EndCall())
        {
            return (Fail(declaration.Name, BindingCode, PackageOperationContext.RefusedMessage(declaration.Name, package, "sent a request through the egress proxy to a host that none of its url bindings declares."), duration), cpuSeconds);
        }

        if (timedOut)
        {
            return (Fail(declaration.Name, TimeoutCode, PackageCallHandler.TimedOut, duration), cpuSeconds);
        }

        var run = reply.Answer switch
        {
            ResultMessage result => new JsonObject
            {
                ["operation"] = declaration.Name,
                ["outcome"] = Succeeded,
                ["output"] = result.Output.DeepClone(),
                ["error"] = null,
                ["durationMs"] = duration,
            },
            RetryableMessage retryable => Fail(declaration.Name, retryable.Error, $"The operation raised {retryable.Error}.", duration),
            FailureMessage { Binding: { } binding } => Fail(declaration.Name, BindingCode, PackageOperationContext.BindingRefusedMessage(declaration.Name, package, binding), duration),
            FailureMessage failure => Fail(declaration.Name, failure.Error ?? nameof(FailureMessage), $"The operation raised {failure.Error ?? nameof(FailureMessage)}.", duration),
            _ => Fail(declaration.Name, ExitedCode, PackageCallHandler.RunnerExited, duration),
        };
        return (run, cpuSeconds);
    }

    // The input must be a JSON object whose values fit the operation's inputs. A field it lacks is null.
    private static Dictionary<string, object?>? ReadValues(string? input, PackageOperation declaration)
    {
        if (input is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(input);
            return document.RootElement.ValueKind == JsonValueKind.Object ? PackageCallHandler.ReadInput(input, declaration) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PackageRunResult Ended(PackageBuildCheck built, string operation, string code, string message) =>
        new([], built.Output, Fail(operation, code, message, 0));

    private static JsonObject Fail(string operation, string code, string message, long durationMs) => new()
    {
        ["operation"] = operation,
        ["outcome"] = Failed,
        ["output"] = null,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        ["durationMs"] = durationMs,
    };
}

/// <summary>
/// The result of <see cref="ApplicationPackageRuns.RunAsync"/>: the diagnostics of a refused or unanswered build, the lines the host wrote, and the
/// <paramref name="Run"/> that the request stores, or <see langword="null"/> when the build had a diagnostic. <paramref name="CpuSeconds"/> are the
/// CPU seconds of the call, rounded up, for the publisher to count in the tenant's package run time, see
/// docs/reference/tenant-quotas.md#package-run-time-a-day. They are <see langword="null"/> when no call went to a runner, and <paramref name="Run"/>
/// never holds them.
/// </summary>
internal sealed record PackageRunResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Output, JsonObject? Run, long? CpuSeconds = null);
