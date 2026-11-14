using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Axis.Configuration.Packages;

/// <summary>
/// A message of the runner protocol, one line of JSON that a <c>type</c> property names, see
/// docs/reference/extension-packages.md#the-runner-protocol. The exact shapes are in
/// <c>sdk/package-runner-protocol.schema.json</c>. No message carries an exception message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StartMessage), "start")]
[JsonDerivedType(typeof(ReadyMessage), "ready")]
[JsonDerivedType(typeof(RunMessage), "run")]
[JsonDerivedType(typeof(ResultMessage), "result")]
[JsonDerivedType(typeof(RetryableMessage), "retryable")]
[JsonDerivedType(typeof(FailureMessage), "failure")]
[JsonDerivedType(typeof(StopMessage), "stop")]
[JsonDerivedType(typeof(SandboxMessage), "sandbox")]
[JsonDerivedType(typeof(BuildMessage), "build")]
[JsonDerivedType(typeof(BuiltMessage), "built")]
[JsonDerivedType(typeof(RefusedMessage), "refused")]
[JsonDerivedType(typeof(TestMessage), "test")]
[JsonDerivedType(typeof(TestedMessage), "tested")]
public abstract record RunnerMessage;

/// <summary>
/// The worker asks the host to start a runner for one installed package version. <paramref name="Proxy"/> is the
/// <c>host:port</c> of the worker's egress proxy, and <paramref name="ProxyToken"/> is the runner's credential for it.
/// </summary>
public sealed record StartMessage(string Package, string Version, string Proxy, string ProxyToken) : RunnerMessage;

/// <summary>The runner has loaded the version and takes a <c>run</c>.</summary>
public sealed record ReadyMessage : RunnerMessage;

/// <summary>The worker asks the runner for one attempt. <paramref name="Input"/> holds values as <see cref="RunnerValues"/> writes them.</summary>
public sealed record RunMessage(string Operation, Guid OperationId, JsonObject Input, RunBindings Bindings) : RunnerMessage;

/// <summary>The binding values of one attempt, each mapping a binding name to its value.</summary>
public sealed record RunBindings(Dictionary<string, string> Urls, Dictionary<string, string> Secrets);

/// <summary>The attempt finished. <paramref name="Output"/> holds values as <see cref="RunnerValues"/> writes them.</summary>
public sealed record ResultMessage(JsonObject Output) : RunnerMessage;

/// <summary>The attempt threw a <c>RetryableException</c>. <paramref name="Error"/> is the full name of its type.</summary>
public sealed record RetryableMessage(string Error) : RunnerMessage;

/// <summary>
/// The attempt failed. It holds the full name of the error's type in <paramref name="Error"/>, or in
/// <paramref name="Binding"/> the undeclared binding that the operation asked for, and never both.
/// </summary>
public sealed record FailureMessage(string? Error = null, string? Binding = null) : RunnerMessage;

/// <summary>The worker is done with the runner.</summary>
public sealed record StopMessage : RunnerMessage;

/// <summary>
/// The worker asks the package host whether it runs under gVisor, with no <paramref name="Gvisor"/>. The host answers
/// with <paramref name="Gvisor"/> set, closes the connection and starts no runner.
/// </summary>
public sealed record SandboxMessage(bool? Gvisor = null) : RunnerMessage;

/// <summary>
/// A caller asks the package host to build the package whose files are in <paramref name="Files"/>, each mapping a
/// relative path that uses <c>/</c> to its content. The content is base64 on the wire.
/// </summary>
public sealed record BuildMessage(Dictionary<string, byte[]> Files) : RunnerMessage;

/// <summary>The host built the package. <paramref name="Files"/> holds every file of the built package, as in <see cref="BuildMessage"/>.</summary>
public sealed record BuiltMessage(Dictionary<string, byte[]> Files) : RunnerMessage;

/// <summary>The host refused the package. Each of <paramref name="Problems"/> names a file and says what is wrong.</summary>
public sealed record RefusedMessage(IReadOnlyList<BuildProblem> Problems) : RunnerMessage;

/// <summary>One problem of a refused build: the <paramref name="File"/> it is about and its <paramref name="Message"/>.</summary>
public sealed record BuildProblem(string File, string Message);

/// <summary>
/// A caller asks the package host to build the package whose files are in <paramref name="Files"/>, as for <see cref="BuildMessage"/>,
/// and then to run the package's own tests in the built copy.
/// </summary>
public sealed record TestMessage(Dictionary<string, byte[]> Files) : RunnerMessage;

/// <summary>The host ran the package's tests. <paramref name="Tests"/> holds the outcome of each test that ran.</summary>
public sealed record TestedMessage(IReadOnlyList<PackageTestOutcome> Tests) : RunnerMessage;

/// <summary>
/// The outcome of one test: its <paramref name="Name"/>, its <paramref name="Outcome"/>, <see cref="Passed"/> or <see cref="Failed"/>, and
/// for a failed test the last line of its traceback in <paramref name="Message"/>, or <see langword="null"/>, which the line writes out.
/// </summary>
public sealed record PackageTestOutcome(string Name, string Outcome, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Message)
{
    public const string Passed = "passed";

    public const string Failed = "failed";
}

/// <summary>Writes and reads the lines of the runner protocol.</summary>
public static class RunnerProtocol
{
    /// <summary>The TCP port that the package host listens on.</summary>
    public const int Port = 5310;

    /// <summary>The default TCP port of the worker's egress proxy.</summary>
    public const int ProxyPort = 5311;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The message as one line of JSON, with no line break in it.</summary>
    public static string Write(RunnerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(message, _options);
    }

    /// <summary>Writes the message and a line break to <paramref name="stream"/> without building the line as a string, so a large message stays cheap.</summary>
    public static async Task WriteAsync(Stream stream, RunnerMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        await JsonSerializer.SerializeAsync(stream, message, _options, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Connects to <paramref name="host"/> and <paramref name="port"/>, sends <paramref name="question"/> as the one line of the
    /// connection and reads the one line that answers it. Returns <see langword="null"/> when the host gives no answer: it refuses or
    /// closes the connection, sends a line that is not a message, or does not answer within <paramref name="wait"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="stopping"/> was cancelled.</exception>
    public static async Task<RunnerMessage?> AskAsync(string host, int port, RunnerMessage question, TimeSpan wait, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(question);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(wait);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            var stream = client.GetStream();
            await WriteAsync(stream, question, timeout.Token);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var line = await reader.ReadLineAsync(timeout.Token);
            return line is null ? null : Read(line);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is SocketException or IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one line.</summary>
    /// <exception cref="JsonException">The line is not a message of the protocol.</exception>
    public static RunnerMessage Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            return JsonSerializer.Deserialize<RunnerMessage>(line, _options) ?? throw new JsonException("The line is not a message.");
        }
        catch (NotSupportedException exception)
        {
            // An unknown type discriminator is reported as an unsupported type.
            throw new JsonException("The line is not a message of the protocol.", exception);
        }
    }

    /// <summary>Reads one line from its UTF-8 bytes, without the line break.</summary>
    /// <exception cref="JsonException">The line is not a message of the protocol.</exception>
    public static RunnerMessage Read(ReadOnlySequence<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            return JsonSerializer.Deserialize<RunnerMessage>(ref reader, _options) ?? throw new JsonException("The line is not a message.");
        }
        catch (NotSupportedException exception)
        {
            // An unknown type discriminator is reported as an unsupported type.
            throw new JsonException("The line is not a message of the protocol.", exception);
        }
    }
}
