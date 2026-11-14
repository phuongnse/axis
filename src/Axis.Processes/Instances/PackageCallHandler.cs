using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Extensions;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the package call a <c>package.call</c> work item names, with no transaction open while the
/// operation runs. A short transaction first marks the call <c>sending</c>, counts the attempt and
/// extends the item's lease by the operation's timeout. The operation then runs from the package
/// version the release pins, with the stored input and the operation identity in its context, under
/// a token that is cancelled at the timeout. An outcome transaction records the result. An output
/// that fits the manifest writes the output fields, starts the data change triggers of the update
/// from the active release, settles the call <c>succeeded</c> and resumes
/// the instance on <c>next</c>. A <see cref="RetryableException"/>, and a pinned version that the
/// worker does not hold, set the call back to <c>pending</c> while the step's retry policy has
/// attempts left. The item is then due again after the backoff, doubled for each attempt and at most
/// an hour, and the next attempt runs under the same operation identity. A retry writes no history row
/// and no audit record. A timeout, and a call found already <c>sending</c>, have an unknown outcome:
/// the operation may have acted, so it is retried only when it is <c>idempotent</c> and attempts are
/// left. Otherwise the call is left <c>unknown</c> and the instance stays waiting. An output that does
/// not fit, any other exception, a release or manifest that does not match, and a retryable error
/// when no attempts are left settle the call <c>failed</c> and fail the instance.
/// Before the operation runs, the worker resolves the <c>url</c> and <c>secret</c> bindings its
/// manifest declares for the tenant. A binding the tenant lacks, or an <c>http</c> URL outside
/// Development, is a retryable error and the operation does not run. The context gives the
/// operation only those bindings. An operation that asks for another binding, or sends a request
/// to a host no declared <c>url</c> binding names, fails the call at once, even if it catches the
/// exception. The error names the operation, the package and the binding, never a value.
/// With <see cref="PackageRunnerPools"/>, a C# package runs in a runner of the C# package host instead of in the worker,
/// and a <c>node</c> package always runs in a runner of the Node.js package host, because the worker cannot load
/// JavaScript, and a <c>python</c> package always runs in a runner of the Python package host, because it cannot load Python,
/// see docs/reference/extension-packages.md#package-runners. The runner gets only the declared bindings, and the same
/// outcomes follow: a runner's <c>retryable</c> and <c>failure</c> answers are the exceptions above, a timeout and a
/// runner that closes its connection are unknown outcomes, and a host that cannot be reached, is not set or starts no
/// runner is a retryable error that names the setting of the call's runtime. A request that a runner sends to a host no declared <c>url</c> binding names is
/// refused by the worker's egress proxy and fails the call at once like the same request from a context. The error
/// names the operation and the package, but no binding and no host.
/// An application's own package is not installed. When the worker finds no installed version, it reads the package's built files from the
/// call's release, in the mark transaction. With them, the attempt goes to a runner of <c>application-package-host</c> at
/// <c>Extensions:Runners:Application</c>, whatever the package's language, which gets the files and the call's tenant, and the same outcomes
/// follow, see docs/reference/extension-packages.md#an-applications-packages. Without a pool the attempt is retryable like one whose host
/// is not set. A release that holds the package's source but no usable built files, such as one that <c>activate</c> stored, fails the call at
/// once: the error names the package and no path, no runner starts and no attempt is counted. A release that holds neither keeps the retryable
/// "not installed" error.
/// The retry policy and the <c>idempotent</c> flag are stored with the call, so a worker that cannot load the pinned
/// version can still decide. Each transaction checks the claim token, and the outcome needs the
/// instance still waiting on the step at the revision the attempt saw. Each outcome transaction
/// also writes one row to <c>axis.process_call_attempts</c>: the attempt with its times, its outcome
/// class and a fixed error text, and no HTTP status. A call found <c>sending</c> gets an attempt row
/// with the outcome <c>unknown</c>. No input value and no exception message is stored or logged.
/// </summary>
internal sealed partial class PackageCallHandler(
    ReleaseModelCache models,
    PackageCatalog packages,
    IDeploymentBindings bindings,
    ConnectorCallSettings settings,
    PackageHttpTransport transport,
    SensitiveValues? sensitive,
    ILogger<PackageCallHandler> logger,
    Func<InstalledPackage, string, IOperation>? createOperation = null,
    PackageRunnerPools? runners = null) : IExternalWorkItemHandler
{
    /// <summary>The error of a call that a worker finds still <c>sending</c> from an earlier attempt.</summary>
    internal const string UnknownOutcome = "The earlier attempt of the call has an unknown outcome.";

    /// <summary>The error of a call whose operation did not finish within its timeout.</summary>
    internal const string TimedOut = "The operation did not finish within its timeout.";

    /// <summary>The error of a call whose runner closed its connection during the attempt.</summary>
    internal const string RunnerExited = "The package runner closed its connection during the attempt.";

    /// <summary>The error of an attempt whose package host, set by <paramref name="setting"/>, could not be reached or started no runner.</summary>
    internal static string RunnerUnreachable(string setting) => $"The package host set by '{setting}' could not be reached or did not start a runner.";

    /// <summary>The error of an attempt when no package host is set in <paramref name="setting"/>.</summary>
    internal static string RunnerNotSet(string setting) => $"No package host is set in '{setting}'.";

    /// <summary>The error of a call whose release holds an application package's source or unusable built files, but no usable built package.</summary>
    internal static string NoBuiltFiles(string package) => $"The release holds no built files of the package '{package}'.";

    public string Kind => ProcessStarts.PackageCallWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        Attempt attempt;
        Target? target = null;
        string? error = null;
        var errorClass = ErrorClass.Failed;
        var counted = true;
        await using (var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken))
        {
            var call = item.PackageCallId is { } callId ? await PackageCalls.LoadAsync(transaction, callId, cancellationToken) : null;
            var instance = call is null ? null : await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
            if (call is not { State: PackageCalls.Pending or PackageCalls.Sending }
                || instance is not { State: ProcessStarts.Waiting }
                || instance.Step != call.Step)
            {
                // The call settled or is unknown, or its instance moved on, so there is nothing to run.
                LogDiscarded(item.Id, item.PackageCallId, call?.State, instance?.State);
                if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.PackageCallId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            attempt = new Attempt(call, instance.Revision, call.Attempts, instance.Now);
            var package = call.State == PackageCalls.Sending ? null : packages.Find(call.Package, call.Version);
            if (call.State == PackageCalls.Sending)
            {
                // A worker crashed after it marked the call, so the operation may have acted. The
                // attempt the crashed worker made is already counted.
                errorClass = ErrorClass.Unknown;
                error = UnknownOutcome;
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                var manifest = package?.Manifest;
                Dictionary<string, byte[]>? files = null;
                if (package is null)
                {
                    // An application's own package is not installed. The release holds its built files, or only its source.
                    var (hasSource, stored) = await ReleasePackageFiles.LoadAsync(transaction, call.ReleaseId, call.Package, cancellationToken);
                    if (stored.Count > 0 || hasSource)
                    {
                        manifest = ReadBuiltManifest(call, stored);
                        files = stored;
                        if (manifest is null)
                        {
                            // No retry can build the files, and the operation never ran, so the attempt is not counted.
                            error = NoBuiltFiles(call.Package);
                            counted = false;
                        }
                    }
                }

                if (manifest is null && error is null)
                {
                    // The release cannot be rebuilt without the version, and the operation never ran, so
                    // the attempt is counted without marking the call.
                    var attempts = await PackageCalls.CountAttemptAsync(transaction, call.Id, cancellationToken)
                        ?? throw new InvalidOperationException($"The package call '{call.Id}' is no longer pending.");
                    attempt = attempt with { Attempts = attempts };
                    errorClass = ErrorClass.Retryable;
                    error = $"The package '{call.Package}' {call.Version} is not installed on this worker.";
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    if (manifest is not null)
                    {
                        try
                        {
                            target = await FindTargetAsync(new WorkItemContext(item, connection, transaction), call, instance, manifest, package, files, cancellationToken);
                            attempt = attempt with { Application = target.Model.Manifest.Name };
                        }
                        catch (InvalidOperationException exception)
                        {
                            // The operation never ran, and the attempt is not counted.
                            error = exception.Message;
                            counted = false;
                        }
                    }

                    if (target is not null)
                    {
                        var attempts = await PackageCalls.MarkSendingAsync(transaction, call.Id, cancellationToken)
                            ?? throw new InvalidOperationException($"The package call '{call.Id}' is no longer pending.");
                        attempt = attempt with { Attempts = attempts };

                        // No other worker claims the item while the operation runs, and a runner starts.
                        var starting = PoolFor(target) is not null ? PackageRunners.StartTimeout : TimeSpan.Zero;
                        if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, starting + target.Step.Timeout + item.Lease, cancellationToken))
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            LogLostClaim(item.Id, item.PackageCallId);
                            return WorkItemOutcome.LostClaim;
                        }

                        await transaction.CommitAsync(cancellationToken);
                    }
                    else
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                }
            }
        }

        if (target is null)
        {
            // A release or a manifest that does not match cannot be fixed by a retry. A call that is
            // found sending or lacks its version is settled by the retry policy.
            return await SettleAttemptAsync(item, connection, attempt, errorClass, error!, counted, cancellationToken);
        }

        if (ReadInput(attempt.Call.Input, target.Operation) is not { } input)
        {
            var misfit = $"The stored input of the call did not fit the operation '{target.Operation.Name}' of the package '{target.Manifest.Id}'.";
            return await FailAsync(item, connection, attempt, misfit, AttemptRow(attempt, CallAttempts.Failed, misfit), cancellationToken);
        }

        using var span = AxisTelemetry.Source.StartActivity(
            AxisTelemetry.PackageCall,
            ActivityKind.Internal,
            default(ActivityContext),
            [
                new(AxisTelemetry.TenantTag, item.TenantId),
                new(AxisTelemetry.ApplicationTag, target.Model.Manifest.Name),
                new(AxisTelemetry.PackageTag, target.Manifest.Id),
                new(AxisTelemetry.OperationTag, target.Operation.Name),
            ]);
        try
        {
            return await RunCallAsync(item, connection, attempt, target, input, cancellationToken);
        }
        catch (Exception exception)
        {
            AxisTelemetry.RecordFailure(span, exception);
            throw;
        }
    }

    /// <summary>Resolves the bindings, runs the operation and settles the call by its result.</summary>
    private async Task<WorkItemOutcome> RunCallAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        Dictionary<string, object?> input,
        CancellationToken cancellationToken)
    {
        // The bindings are resolved before the operation runs, so a missing one leaves nothing run.
        if (!TryResolveBindings(item, target, out var urls, out var secrets, out var bindingError))
        {
            return await SettleAttemptAsync(item, connection, attempt, ErrorClass.Retryable, bindingError, true, cancellationToken);
        }

        // A package that has a runner pool never loads in the worker, see
        // docs/reference/extension-packages.md#package-runners. An application's own package never runs in the worker, so without
        // a pool its attempt is retryable, like one whose host is not set.
        Run run;
        if (PoolFor(target) is { } pool)
        {
            run = await RunInRunnerAsync(pool, item.TenantId, target, attempt.Call, input, urls, secrets, cancellationToken);
        }
        else if (target.Files is not null)
        {
            run = new Run(null, null, Error: RunnerNotSet(ApplicationPackageSettings.RunnerSetting), Retryable: true);
        }
        else
        {
            run = await RunAsync(target, attempt.Call, input, urls, secrets, cancellationToken);
        }

        if (run.Violation is { } violation)
        {
            // The operation asked for more than it declares. A retry cannot fix that, and the
            // operation may have swallowed the exception, so this holds whatever it returned.
            return await FailAsync(item, connection, attempt, violation, AttemptRow(attempt, CallAttempts.Failed, violation), cancellationToken);
        }

        if (run.Timeout)
        {
            // The operation may have acted.
            return await SettleAttemptAsync(item, connection, attempt, ErrorClass.Unknown, TimedOut, true, cancellationToken);
        }

        if (run.Exited)
        {
            // The runner may have acted before it exited.
            return await SettleAttemptAsync(item, connection, attempt, ErrorClass.Unknown, RunnerExited, true, cancellationToken);
        }

        if (run.Error is { } runError)
        {
            return await SettleAttemptAsync(item, connection, attempt, run.Retryable ? ErrorClass.Retryable : ErrorClass.Failed, runError, true, cancellationToken);
        }

        if (Fit(run.Output, target) is not { } values)
        {
            var misfit = $"The output of the operation '{target.Operation.Name}' of the package '{target.Manifest.Id}' {target.Manifest.Version} did not fit its manifest.";
            return await FailAsync(item, connection, attempt, misfit, AttemptRow(attempt, CallAttempts.Failed, misfit), cancellationToken);
        }

        return await SucceedAsync(item, connection, attempt, target, values, cancellationToken);
    }

    /// <summary>
    /// Finds the step, the subject entity and the operation of the call in the release and the
    /// <paramref name="manifest"/> of its package, which is the installed <paramref name="installed"/> package or, with <paramref name="files"/>,
    /// the application's own. A process without a subject has no entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The release or the package lacks one of them.</exception>
    private async Task<Target> FindTargetAsync(
        WorkItemContext context,
        LoadedPackageCall call,
        LoadedInstance instance,
        PackageManifest manifest,
        InstalledPackage? installed,
        Dictionary<string, byte[]>? files,
        CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(context, call.ReleaseId, cancellationToken);
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)
            ?? throw new InvalidOperationException($"The release has no process '{instance.ProcessId}'.");
        var entity = instance.SubjectEntityId is { } entityId
            ? model.FindEntity(entityId) ?? throw new InvalidOperationException($"The release has no entity '{entityId}'.")
            : null;
        if (!process.TryGetStep(call.Step, out var step) || step is not PackageOperationStepModel packageStep)
        {
            throw new InvalidOperationException($"The process '{process.Name}' has no package operation step '{call.Step}'.");
        }

        var operation = manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The package '{call.Package}' {call.Version} has no operation '{call.Operation}'.");
        return new Target(model, entity, packageStep, manifest, operation, installed, files);
    }

    /// <summary>
    /// The runner pool that runs the call's package: the application pool for an application's own package, the pool of
    /// its language for an installed one, or <see langword="null"/> when there is none and the worker runs it itself.
    /// </summary>
    private PackageRunners? PoolFor(Target target) =>
        target.Files is not null ? runners?.Application : runners?.For(target.Manifest.Language);

    /// <summary>
    /// Reads the manifest of the application package that <paramref name="call"/> names from its built <paramref name="files"/>. Returns
    /// <see langword="null"/> when they hold no manifest, the manifest cannot be read, it names another id or version than the call, or it says the
    /// package runs in a customer-hosted runner, so the files cannot start a runner.
    /// </summary>
    private static PackageManifest? ReadBuiltManifest(LoadedPackageCall call, Dictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(PackageCatalog.ManifestFileName, out var content))
        {
            return null;
        }

        try
        {
            return PackageCatalog.ReadManifestText(new UTF8Encoding(false, true).GetString(content)) is { CustomerHosted: false } manifest
                && manifest.Id == call.Package
                && manifest.Version == call.Version
                    ? manifest
                    : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the stored input into the .NET values the operation gets, one for each input of its
    /// manifest, in the form the expression interpreter gives. An input the call stores as null, or
    /// not at all, is <see langword="null"/>. Returns <see langword="null"/> when the input is not
    /// stored or a value has the wrong type.
    /// </summary>
    private static Dictionary<string, object?>? ReadInput(string? stored, PackageOperation operation)
    {
        if (stored is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(stored);
        var values = new Dictionary<string, object?>();
        foreach (var field in operation.Input)
        {
            object? value = null;
            if (document.RootElement.TryGetProperty(field.Name, out var element)
                && element.ValueKind != JsonValueKind.Null
                && !ConnectorCallHandler.TryRead(element, field.Type, out value))
            {
                return null;
            }

            values[field.Name] = value;
        }

        return values;
    }

    /// <summary>
    /// Resolves the <c>url</c> and <c>secret</c> bindings the operation declares for the current
    /// tenant. Returns <see langword="false"/> with the error when a binding is
    /// missing or of the wrong kind, or when a URL is not <c>https</c> and the host does not allow
    /// <c>http</c>. The error never holds a binding's value.
    /// </summary>
    private bool TryResolveBindings(
        ClaimedWorkItem item,
        Target target,
        out Dictionary<string, Uri> urls,
        out Dictionary<string, Func<string>> secrets,
        [NotNullWhen(false)] out string? error)
    {
        var capabilities = target.Operation.Capabilities;
        urls = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        secrets = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in capabilities.Urls)
            {
                var value = bindings.Resolve(name).Value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !settings.AllowHttp))
                {
                    error = $"Tenant '{item.TenantId}' binding '{name}' is not an https URL. Only Development allows http.";
                    return false;
                }

                urls[name] = uri;
            }

            foreach (var name in capabilities.Secrets)
            {
                secrets[name] = bindings.ResolveSecret(name).Reveal;
            }
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the operation and runs it in the worker, with no transaction open, under a token that is
    /// cancelled at the step's timeout. The result holds the output, or the error of an exception as its type
    /// name and whether it is retryable, or says that the run timed out, and the violation of the declared
    /// capabilities that the context kept. Worker shutdown is not caught.
    /// </summary>
    private async Task<Run> RunAsync(
        Target target,
        LoadedPackageCall call,
        Dictionary<string, object?> input,
        Dictionary<string, Uri> urls,
        Dictionary<string, Func<string>> secrets,
        CancellationToken cancellationToken)
    {
        var context = new PackageOperationContext(call.Id, target.Manifest.Id, target.Operation.Name, urls, secrets, transport.Handler);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(target.Step.Timeout);
        try
        {
            var installed = target.Installed!;
            var operation = createOperation is null ? installed.CreateOperation(call.Operation) : createOperation(installed, call.Operation);

            // An operation that ignores its token keeps running, but the worker stops waiting for it.
            var output = await operation.RunAsync(input, context, timeout.Token).WaitAsync(timeout.Token);
            return new Run(output, context.Violation);
        }
        catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new Run(null, context.Violation, Timeout: true);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The message can hold input values or secrets, so only the type is kept.
            return new Run(null, context.Violation, Error: ErrorOf(exception), Retryable: exception is RetryableException);
        }
    }

    /// <summary>
    /// Sends the attempt to a runner of the package host, see docs/reference/extension-packages.md#package-runners. The
    /// step's timeout starts when the <c>run</c> is sent, and the start of a runner has its own bound. The <c>run</c> holds the stored input and
    /// the declared bindings of the call's tenant, nothing else. An attempt that fails before the
    /// <c>run</c> is sent is retryable, because the operation never ran. After it, a timeout closes the
    /// connection, which makes the host kill the runner, and a runner that closes its connection exited. Both
    /// are unknown outcomes. A runner that answered goes back to the pool. Worker shutdown is not caught. While the
    /// call runs, the worker's egress proxy admits only the hosts of the declared <c>url</c> bindings. When it refused a
    /// request of the runner, the attempt is a violation whatever the runner answered or whether it timed out.
    /// </summary>
    private async Task<Run> RunInRunnerAsync(
        PackageRunners pool,
        string tenant,
        Target target,
        LoadedPackageCall call,
        Dictionary<string, object?> input,
        Dictionary<string, Uri> urls,
        Dictionary<string, Func<string>> secrets,
        CancellationToken cancellationToken)
    {
        if (!pool.IsSet)
        {
            return new Run(null, null, Error: RunnerNotSet(pool.Setting), Retryable: true);
        }

        RunMessage message;
        try
        {
            message = new RunMessage(
                target.Operation.Name,
                call.Id,
                RunnerValues.Encode(target.Operation.Input, input),
                new RunBindings(
                    urls.ToDictionary(pair => pair.Key, pair => pair.Value.AbsoluteUri),
                    secrets.ToDictionary(pair => pair.Key, pair => pair.Value())));
        }
        catch (InvalidCastException exception)
        {
            return new Run(null, null, Error: ErrorOf(exception));
        }

        RunnerConnection runner;
        using (var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            starting.CancelAfter(PackageRunners.StartTimeout);
            try
            {
                runner = await pool.RentAsync(call.Package, call.Version, starting.Token, target.Files is null ? null : tenant, target.Files);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A host that cannot be reached, or starts no runner, and a start that takes too long.
                return new Run(null, null, Error: RunnerUnreachable(pool.Setting), Retryable: true);
            }
        }

        // The runner's egress proxy admits the hosts of the declared url bindings for this call only.
        runner.Admit(urls.Values);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(target.Step.Timeout);
        RunnerMessage? answer = null;
        var timedOut = false;
        try
        {
            answer = await runner.RunAsync(message, timeout.Token);
        }
        catch (Exception)
        {
            // The timeout or the shutdown. Closing the connection makes the host kill the runner.
            if (cancellationToken.IsCancellationRequested)
            {
                runner.Dispose();
                throw;
            }

            timedOut = true;
        }

        // A request that the proxy refused fails the call at once, even if the package caught the error. Such a
        // runner is closed, so it never serves another call.
        var refused = runner.EndCall();
        if (refused || timedOut || answer is null)
        {
            runner.Dispose();
        }

        if (refused)
        {
            return new Run(
                null,
                PackageOperationContext.RefusedMessage(target.Operation.Name, target.Manifest.Id, "sent a request through the egress proxy to a host that none of its url bindings declares."));
        }

        if (timedOut)
        {
            return new Run(null, null, Timeout: true);
        }

        if (answer is null)
        {
            return new Run(null, null, Exited: true);
        }

        pool.Return(runner);
        switch (answer)
        {
            case ResultMessage result:
                try
                {
                    // Decode gives null for an output the result lacks, which Fit would write as an explicit null.
                    var output = RunnerValues.Decode(target.Operation.Output, result.Output);
                    foreach (var field in target.Operation.Output.Where(field => !result.Output.ContainsKey(field.Name)))
                    {
                        output.Remove(field.Name);
                    }

                    return new Run(output, null);
                }
                catch (FormatException)
                {
                    return new Run(null, null);
                }

            case RetryableMessage retryable:
                return new Run(null, null, Error: retryable.Error, Retryable: true);
            case FailureMessage { Binding: { } binding }:
                return new Run(
                    null,
                    PackageOperationContext.RefusedMessage(target.Operation.Name, target.Manifest.Id, $"went beyond its declared capabilities with the binding '{binding}'."));
            case FailureMessage failure:
                return new Run(null, null, Error: failure.Error ?? nameof(FailureMessage));
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>The full type name of <paramref name="exception"/>, the only part of it that is recorded.</summary>
    private static string ErrorOf(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    /// <summary>
    /// Reads the outputs from the operation's result. The result fits when it holds a value of the
    /// exact .NET type for each output it holds, and a value for each required output. Returns the
    /// subject fields to write with their values, or <see langword="null"/> when it does not fit.
    /// An optional output the result leaves out writes nothing, and an explicit <c>null</c> writes
    /// <c>null</c>.
    /// </summary>
    private static List<KeyValuePair<FieldModel, object?>>? Fit(IReadOnlyDictionary<string, object?>? result, Target target)
    {
        if (result is null)
        {
            return null;
        }

        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var output in target.Operation.Output)
        {
            var present = result.TryGetValue(output.Name, out var value);
            if (value is null)
            {
                if (output.Required)
                {
                    return null;
                }
            }
            else if (!HasType(value, output.Type))
            {
                return null;
            }

            var mapping = target.Step.Output.FirstOrDefault(candidate => string.Equals(candidate.Output, output.Name, StringComparison.OrdinalIgnoreCase));
            if (present && mapping is not null)
            {
                if (target.Entity is not { } entity || !entity.TryGetField(mapping.Field, out var field))
                {
                    return null;
                }

                values.Add(KeyValuePair.Create(field, value is DateTimeOffset time ? time.ToUniversalTime() : value));
            }
        }

        return values;
    }

    private static bool HasType(object value, FieldType type) =>
        type switch
        {
            FieldType.Text => value is string,
            FieldType.Integer => value is long,
            FieldType.Decimal => value is decimal,
            FieldType.Boolean => value is bool,
            FieldType.Date => value is DateOnly,
            FieldType.DateTime => value is DateTimeOffset,
            _ => false,
        };

    /// <summary>
    /// Records a fitting output in one transaction: the output fields with the record update rules,
    /// the call <c>succeeded</c>, the instance <c>running</c> on <c>next</c>, the history row, the
    /// audit records, the next step's work item and the attempt row. A refused write or any other
    /// error rolls it back and fails the call instead, and the attempt row then says it failed.
    /// </summary>
    private async Task<WorkItemOutcome> SucceedAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        Target target,
        List<KeyValuePair<FieldModel, object?>> values,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        string? failure;
        await using (var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken))
        {
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, call.Id);
                return WorkItemOutcome.LostClaim;
            }

            var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
            if (!IsWaitingAt(instance, call, attempt))
            {
                LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            try
            {
                failure = await WriteSuccessAsync(item, transaction, instance!, attempt, target, values, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The message can hold record values, so only the type is kept.
                failure = ErrorOf(exception);
            }

            if (failure is null)
            {
                await transaction.CommitAsync(cancellationToken);
                LogSucceeded(call.Id, call.ProcessInstanceId);
                return WorkItemOutcome.Completed;
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        return await FailAsync(item, connection, attempt, failure, AttemptRow(attempt, CallAttempts.Failed, failure), cancellationToken);
    }

    /// <summary>Writes the success in the transaction. Returns the error when the record write is refused, or <see langword="null"/>.</summary>
    private async Task<string?> WriteSuccessAsync(
        ClaimedWorkItem item,
        NpgsqlTransaction transaction,
        LoadedInstance instance,
        Attempt attempt,
        Target target,
        List<KeyValuePair<FieldModel, object?>> values,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var connection = transaction.Connection!;
        var entity = target.Entity;
        var cipher = sensitive is null ? null : new RecordCipher(sensitive, item.TenantId);
        Record? record = null;

        // A process without a subject has no record to lock, read or write, and no outputs to write.
        if (instance.SubjectId is { } subjectId && entity is not null)
        {
            if (values.Count > 0)
            {
                // The write can start instances through data change triggers. The start lock comes before any
                // entity table is read, so a migrating activation cannot deadlock with this transaction.
                await StartLock.TakeSharedAsync(transaction, instance.ApplicationId, cancellationToken);

                // The lock makes the read below see the latest version, and a concurrent write of the
                // record waits until this transaction ends.
                await RecordCommands.LockAsync(connection, entity, subjectId, cancellationToken);
            }

            record = await RecordQueries.GetAsync(connection, target.Model, entity, subjectId, cipher, cancellationToken);
            if (record is null && values.Count > 0)
            {
                return $"No record of '{entity.Name}' has the id '{subjectId}'.";
            }
        }
        else if (values.Count > 0)
        {
            return "The step has outputs, but the process has no subject to write them to.";
        }

        if (!await ProcessSteps.TryResumeCallAsync(transaction, instance.Id, call.Step, attempt.Revision, target.Step.Next, cancellationToken))
        {
            throw new InvalidOperationException($"The process instance '{instance.Id}' is no longer waiting on the step '{call.Step}'.");
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = record?.Version };
        var output = new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Succeeded, ["next"] = target.Step.Next };
        AuditEntry? recordAudit = null;
        Record? updated = null;
        IReadOnlyList<EffectRecord> effects = [];
        var write = new RecordWriteContext(instance.AccountableUser, instance.DecisionTask);
        if (values.Count > 0)
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var result = await RecordCommands.SetAsync(connection, target.Model, entity!, call.ReleaseId, write, record!, values, now, cipher: cipher, cancellationToken: cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                return $"The record update was rejected: {ProcessStepHandler.Describe(result)}";
            }

            await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, item.TenantId, target.Model, entity!, call.ReleaseId, cancellationToken);
            updated = written;
            effects = result.Effects;
            output["subjectVersion"] = written.Version;
            recordAudit = new AuditEntry(
                AuditActors.System,
                AuditActions.RecordUpdated,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject
                {
                    ["version"] = written.Version,
                    ["fields"] = new JsonArray([.. values.Select(value => JsonValue.Create(value.Key.Name))]),
                });
        }

        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Succeeded, null, cancellationToken);
        await CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, AttemptRow(attempt, CallAttempts.Succeeded, null), Tags(item, attempt), cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(instance.Id, call.Step, attempt.Revision, input, output, null, null, attempt.StartedAt),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(transaction, recordAudit, cancellationToken);
        }

        if (updated is not null)
        {
            var active = await models.FindActiveAsync(new WorkItemContext(item, connection, transaction), instance.ApplicationId, cancellationToken);
            if (active is { } activeNow)
            {
                await DataChangeTriggerStarts.StartAfterUpdateAsync(
                    transaction,
                    item.TenantId,
                    activeNow.Model,
                    activeNow.ReleaseId,
                    call.ReleaseId,
                    entity!,
                    record!,
                    updated,
                    [],
                    write,
                    logger,
                    cancellationToken);
            }

            await EffectRecordWrites.AppendAsync(
                transaction,
                item.TenantId,
                active?.Model,
                active?.ReleaseId ?? call.ReleaseId,
                call.ReleaseId,
                instance.ApplicationId,
                instance.Id,
                AuditActors.System,
                effects,
                write,
                logger,
                cancellationToken);
        }

        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, PackageCalls.Succeeded), cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instance.Id);
        return null;
    }

    /// <summary>
    /// Records an attempt that ended with <paramref name="error"/> of <paramref name="errorClass"/>.
    /// A retryable error is retried while attempts are left, and an unknown outcome only when the
    /// operation is also <c>idempotent</c>. An unknown outcome that is not retried leaves the call
    /// <c>unknown</c>. Anything else fails the call. The attempt row records the class of the attempt,
    /// whatever happens to the call. It is left out when <paramref name="counted"/> is
    /// <see langword="false"/>, because no attempt was made.
    /// </summary>
    private Task<WorkItemOutcome> SettleAttemptAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        ErrorClass errorClass,
        string error,
        bool counted,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        var attemptsLeft = attempt.Attempts < call.Retry.Attempts;
        var row = counted
            ? AttemptRow(
                attempt,
                errorClass switch
                {
                    ErrorClass.Retryable => CallAttempts.Retryable,
                    ErrorClass.Unknown => CallAttempts.Unknown,
                    _ => CallAttempts.Failed,
                },
                error)
            : null;
        return errorClass switch
        {
            ErrorClass.Retryable when attemptsLeft =>
                RetryAsync(item, connection, attempt, error, ConnectorCallHandler.Backoff(call.Retry, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown when attemptsLeft && call.Idempotent =>
                RetryAsync(item, connection, attempt, error, ConnectorCallHandler.Backoff(call.Retry, attempt.Attempts), row, cancellationToken),
            ErrorClass.Unknown => LeaveUnknownAsync(item, connection, attempt, error, row, cancellationToken),
            _ => FailAsync(item, connection, attempt, error, row, cancellationToken),
        };
    }

    /// <summary>
    /// Sets the call back to <c>pending</c> with the attempt's error, and keeps the item, due again
    /// after <paramref name="delay"/>. The next attempt runs the stored input under the same
    /// operation identity. It writes <paramref name="row"/>, and no history row and no audit record.
    /// </summary>
    private async Task<WorkItemOutcome> RetryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        TimeSpan delay,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        if (!IsWaitingAt(instance, call, attempt))
        {
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, call.Id);
                return WorkItemOutcome.LostClaim;
            }

            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await PackageCalls.RetryAsync(transaction, call.Id, error, cancellationToken);
        await InsertAttemptAsync(transaction, Tags(item, attempt), call, row, cancellationToken);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogRetrying(call.Id, call.ProcessInstanceId, attempt.Attempts, error, delay);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Leaves the call <c>unknown</c> with the attempt's error, and keeps its stored input. The
    /// item is deleted and the instance stays waiting. A history row with the outcome
    /// <c>unknown</c> makes the call visible. It writes no audit record, because the call has not
    /// settled. It writes <paramref name="row"/>.
    /// </summary>
    private async Task<WorkItemOutcome> LeaveUnknownAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        var waiting = IsWaitingAt(instance, call, attempt);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        if (!waiting)
        {
            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await PackageCalls.MarkUnknownAsync(transaction, call.Id, error, cancellationToken);
        await InsertAttemptAsync(transaction, Tags(item, attempt), call, row, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance!.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Unknown },
                null,
                error,
                attempt.StartedAt),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogUnknown(call.Id, instance.Id, attempt.Attempts, error);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Settles the call <c>failed</c> with <paramref name="error"/> and fails its instance, with a
    /// history row and the audit records <c>operation.called</c> and <c>process.failed</c>. It writes
    /// <paramref name="row"/> unless it is <see langword="null"/>, which means that no attempt was made.
    /// </summary>
    private async Task<WorkItemOutcome> FailAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Attempt attempt,
        string error,
        CallAttempt? row,
        CancellationToken cancellationToken)
    {
        var call = attempt.Call;
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, call.Id);
            return WorkItemOutcome.LostClaim;
        }

        var instance = await ProcessSteps.LoadAsync(transaction, call.ProcessInstanceId, cancellationToken);
        if (!IsWaitingAt(instance, call, attempt)
            || !await ProcessSteps.TryFailWaitingAsync(transaction, instance!.Id, call.Step, attempt.Revision, cancellationToken))
        {
            LogStale(call.Id, call.ProcessInstanceId, call.Step, attempt.Revision);
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }

        await InstanceMetrics.RecordEndedAsync(models, new WorkItemContext(item, connection, transaction), instance, ProcessStarts.Failed, cancellationToken);
        await PackageCalls.SettleAsync(transaction, call.Id, PackageCalls.Failed, error, cancellationToken);
        await InsertAttemptAsync(transaction, Tags(item, attempt), call, row, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            transaction,
            new StepOccurrence(
                instance.Id,
                call.Step,
                attempt.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["operationId"] = call.Id, ["outcome"] = PackageCalls.Failed },
                null,
                error,
                attempt.StartedAt),
            cancellationToken);
        await AuditRecords.AppendAsync(transaction, Called(instance, attempt, PackageCalls.Failed), cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessFailed,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject { ["step"] = call.Step }),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogFailed(call.Id, instance.Id, error);
        return WorkItemOutcome.Failed;
    }

    /// <summary>
    /// The row of the attempt the worker makes. Its number is the next attempt number of the call, which
    /// carries on after an operator retry. A package call has no HTTP status.
    /// </summary>
    private static CallAttempt AttemptRow(Attempt attempt, string outcome, string? error) =>
        new(CallAttempts.Attempt, null, attempt.StartedAt, outcome, null, error);

    private static CallTags Tags(ClaimedWorkItem item, Attempt attempt) =>
        new(item.TenantId, attempt.Application, AxisTelemetry.PackageTag, attempt.Call.Package, attempt.Call.Operation);

    private static Task InsertAttemptAsync(NpgsqlTransaction transaction, CallTags tags, LoadedPackageCall call, CallAttempt? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : CallAttempts.InsertAsync(transaction, CallAttempts.Package, call.Id, row, tags, cancellationToken);

    private static bool IsWaitingAt(LoadedInstance? instance, LoadedPackageCall call, Attempt attempt) =>
        instance is { State: ProcessStarts.Waiting } && instance.Step == call.Step && instance.Revision == attempt.Revision;

    private static AuditEntry Called(LoadedInstance instance, Attempt attempt, string outcome) =>
        new(
            AuditActors.System,
            AuditActions.OperationCalled,
            instance.ApplicationId,
            instance.SubjectEntityId,
            instance.SubjectId,
            instance.Id,
            new JsonObject
            {
                ["package"] = attempt.Call.Package,
                ["version"] = attempt.Call.Version,
                ["operation"] = attempt.Call.Operation,
                ["operationId"] = attempt.Call.Id,
                ["attempts"] = attempt.Attempts,
                ["outcome"] = outcome,
            });

    [LoggerMessage(LogLevel.Warning, "Package call work item {ItemId} for call {CallId} was discarded: the call is {CallState} and its instance is {InstanceState}.")]
    private partial void LogDiscarded(Guid itemId, Guid? callId, string? callState, string? instanceState);

    [LoggerMessage(LogLevel.Warning, "Package call work item {ItemId} for call {CallId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? callId);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} was made at revision {Revision} of step {Step}, but the instance changed since, so its outcome was not recorded.")]
    private partial void LogStale(Guid callId, Guid instanceId, string step, long revision);

    [LoggerMessage(LogLevel.Information, "Package call {CallId} of process instance {InstanceId} succeeded.")]
    private partial void LogSucceeded(Guid callId, Guid instanceId);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} failed: {Error}")]
    private partial void LogFailed(Guid callId, Guid instanceId, string error);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} will be retried after attempt {Attempts} in {Delay}: {Error}")]
    private partial void LogRetrying(Guid callId, Guid instanceId, int attempts, string error, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Package call {CallId} of process instance {InstanceId} has an unknown outcome after {Attempts} attempts and is not run again: {Error}")]
    private partial void LogUnknown(Guid callId, Guid instanceId, int attempts, string error);

    /// <summary>
    /// The attempt a worker makes: the call, the instance revision it waits at, the attempts made
    /// with this one, and the time of the mark transaction, which starts the step's history row.
    /// </summary>
    private sealed record Attempt(LoadedPackageCall Call, long Revision, int Attempts, DateTimeOffset StartedAt, string? Application = null);

    /// <summary>
    /// What the call's release and the host declare for it: the manifest of its package, and either the installed package, or for an
    /// application's own package the built files that the release stores.
    /// </summary>
    private sealed record Target(
        ApplicationModel Model,
        EntityModel? Entity,
        PackageOperationStepModel Step,
        PackageManifest Manifest,
        PackageOperation Operation,
        InstalledPackage? Installed = null,
        Dictionary<string, byte[]>? Files = null);

    /// <summary>
    /// The result of a run: the output, the violation of the declared capabilities, the error of an exception and
    /// whether it is retryable, that it timed out, or that its runner exited.
    /// </summary>
    private sealed record Run(
        IReadOnlyDictionary<string, object?>? Output,
        string? Violation,
        string? Error = null,
        bool Retryable = false,
        bool Timeout = false,
        bool Exited = false);

    /// <summary>How an attempt that did not succeed is handled.</summary>
    private enum ErrorClass
    {
        /// <summary>The call fails at once.</summary>
        Failed,

        /// <summary>The operation did not act, so the call is retried while attempts are left.</summary>
        Retryable,

        /// <summary>The operation may have acted. The call is retried only when it is <c>idempotent</c>.</summary>
        Unknown,
    }
}
