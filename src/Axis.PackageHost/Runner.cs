using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Extensions;

namespace Axis.PackageHost;

/// <summary>
/// Runner mode: runs the operations of one installed C# package version, one <c>run</c> at a time, see
/// docs/reference/extension-packages.md#the-runner-protocol. The protocol goes over the standard input and
/// output. Package code that writes to the console writes to standard error, so it cannot corrupt the protocol
/// stream. The exit code is 0 when the input ends or a <c>stop</c> arrives, 1 when the version does not load,
/// and 2 when a line is not a <c>run</c> or a <c>stop</c>. Each <c>run</c> gets exactly one answer. The context
/// hands out only the bindings of the <c>run</c>, so an operation that asks for another binding gets a
/// <c>failure</c> with that binding, even when it catches the exception. Any other error is a <c>failure</c> with the
/// full name of its type, never its message.
/// </summary>
internal static class Runner
{
    public const int Stopped = 0;
    public const int NotLoaded = 1;
    public const int BadInput = 2;

    public static async Task<int> RunAsync(string root, string id, string version)
    {
        // The protocol keeps the real standard output. Console output of package code goes to standard error.
        await using var protocolOut = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);

        var (package, diagnostics) = PackageCatalog.LoadVersion(root, id, version);
        if (package is null)
        {
            foreach (var diagnostic in diagnostics)
            {
                await Console.Error.WriteLineAsync($"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}");
            }

            return NotLoaded;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var output = new StreamWriter(protocolOut, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var transport = new PackageHttpTransport();
        await output.WriteLineAsync(RunnerProtocol.Write(new ReadyMessage()));

        while (await input.ReadLineAsync() is { } line)
        {
            RunnerMessage message;
            try
            {
                message = RunnerProtocol.Read(line);
            }
            catch (JsonException)
            {
                return BadInput;
            }

            switch (message)
            {
                case RunMessage run:
                    await output.WriteLineAsync(RunnerProtocol.Write(await AnswerAsync(package, transport, run)));
                    break;
                case StopMessage:
                    return Stopped;
                default:
                    return BadInput;
            }
        }

        return Stopped;
    }

    /// <summary>The one answer to <paramref name="run"/>.</summary>
    private static async Task<RunnerMessage> AnswerAsync(InstalledPackage package, PackageHttpTransport transport, RunMessage run)
    {
        PackageOperationContext? context = null;
        IReadOnlyDictionary<string, object?>? output = null;
        Exception? error = null;
        PackageOperation? declared = null;
        try
        {
            // An unknown operation throws an ArgumentException.
            var operation = package.CreateOperation(run.Operation);
            declared = package.Manifest.Operations.First(candidate => string.Equals(candidate.Name, run.Operation, StringComparison.OrdinalIgnoreCase));
            var input = RunnerValues.Decode(declared.Input, run.Input);
            context = new PackageOperationContext(
                run.OperationId,
                package.Manifest.Id,
                declared.Name,
                run.Bindings.Urls.ToDictionary(pair => pair.Key, pair => new Uri(pair.Value, UriKind.Absolute), StringComparer.OrdinalIgnoreCase),
                run.Bindings.Secrets.ToDictionary(pair => pair.Key, pair => (Func<string>)(() => pair.Value), StringComparer.OrdinalIgnoreCase),
                transport.Handler);
            output = await operation.RunAsync(input, context, CancellationToken.None);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        // The operation may have swallowed the exception, so a refused binding fails the attempt whatever it returned.
        if (context?.ViolationBinding is { } binding)
        {
            return new FailureMessage(null, binding);
        }

        if (error is not null)
        {
            // The message can hold input values or secrets, so only the type is sent.
            return error is RetryableException
                ? new RetryableMessage(ErrorOf(error))
                : new FailureMessage(ErrorOf(error), null);
        }

        try
        {
            return new ResultMessage(RunnerValues.Encode(declared!.Output, output ?? throw new InvalidCastException("The operation returned no output.")));
        }
        catch (Exception exception) when (exception is InvalidCastException)
        {
            return new FailureMessage(ErrorOf(exception), null);
        }
    }

    private static string ErrorOf(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;
}
